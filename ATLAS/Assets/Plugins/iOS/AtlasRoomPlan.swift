import ARKit
import Foundation
import RoomPlan

// The one RoomPlan session of a scan (standalone-phone plan D2, section 6.2): walls, doors, windows, openings,
// furniture objects with their attributes, and RoomPlan's coaching instruction, from a single RoomCaptureSession
// that runs on Unity's own ARSession. Nothing else in the app starts a RoomCaptureSession: a second one crashed
// the scan on the device (4 October 2026). Every update of the captured room is kept as one small JSON file,
// which the C# side (Atlas.Scan.ARKit.ArKitRoomPlan) copies out and Atlas.World.RoomPlanJson reads:
//   {"schema":"atlas.room/1","walls":[S,...],"doors":[S,...],"windows":[S,...],"openings":[S,...],
//    "objects":[O,...],"instruction":"normal|moveCloseToWall|moveAwayFromWall|slowDown|turnOnLight|lowTexture"}
//   S = {"c":[x,y,z],"x":[x,y,z],"w":width,"h":height}
//   O = {"id":"<uuid>","cat":"<category>","c":[x,y,z],"x":[x,y,z],"z":[x,y,z],"d":[w,h,d],
//        "conf":"low|medium|high","attrs":["..."]}
// c is the centre (column 3 of the transform), x and z the axis columns 0 and 2, in ARKit's world frame; w, h
// and d are dimensions, metres. cat is the RoomPlan category's case name (sofa, television, washerDryer, ...);
// attrs are the raw values of the object's CapturedRoomAttribute values, verbatim. The reader turns the frame
// into Unity's.
// The four C functions are called on the main thread. RoomPlan's callbacks may come on another, hence the lock.
// The project targets iOS 17, which RoomCaptureSession(arSession:), stop(pauseARSession:) and attributes need.

final class AtlasRoomPlanBridge: NSObject, RoomCaptureSessionDelegate {
    static let shared = AtlasRoomPlanBridge()

    // Spike S1 (open question 2): whether attributes arrive with didUpdate or only from RoomBuilder. When the
    // capture ends, the room is built once more from its data with RoomBuilder, off the main thread, and the
    // attributes it gives are logged next to those didUpdate gave. Logging only: nothing reads the result.
    private static let logBuiltAttributes = true

    private let lock = NSLock()
    private var walls = "[]", doors = "[]", windows = "[]", openings = "[]", objects = "[]"
    private var instruction = "normal"
    private var json = [UInt8]()
    private var lastSummary = ""
    private var capture: RoomCaptureSession?
    // A stopped session whose didEndWith has not come yet (logBuiltAttributes).
    private var ending: RoomCaptureSession?

    // capture and ending are written on the main thread and read in RoomPlan's callbacks: both under the lock.
    func start(_ arSession: ARSession) -> Bool {
        if current() != nil { return true }
        guard RoomCaptureSession.isSupported else { return false }
        let session = RoomCaptureSession(arSession: arSession)
        lock.lock()
        walls = "[]"; doors = "[]"; windows = "[]"; openings = "[]"; objects = "[]"
        instruction = "normal"
        json.removeAll()
        lastSummary = ""
        capture = session
        lock.unlock()
        session.delegate = self
        session.run(configuration: RoomCaptureSession.Configuration())
        AtlasRoomPlanBridge.log("Atlas RoomPlan: capture started on Unity's ARSession")
        return true
    }

    func stop() {
        lock.lock()
        let running = capture
        capture = nil
        if AtlasRoomPlanBridge.logBuiltAttributes { ending = running }     // keeps the delegate for didEndWith
        lock.unlock()
        guard let session = running else { return }
        if !AtlasRoomPlanBridge.logBuiltAttributes { session.delegate = nil }
        let arSession = session.arSession
        session.stop(pauseARSession: false)
        // RoomPlan leaves the session on its own configuration, and frames carry no scene depth until a
        // configuration is run again. Run the current one again with scene depth asked for; Unity then
        // applies its own on the next change of a feature (the scan session calls ArSessionRig.ApplyDepth).
        if let configuration = arSession.configuration {
            if type(of: configuration).supportsFrameSemantics(.sceneDepth) {
                configuration.frameSemantics.insert(.sceneDepth)
            }
            arSession.run(configuration)
        }
        AtlasRoomPlanBridge.log("Atlas RoomPlan: capture stopped, the ARSession runs on")
    }

    func copyJson(_ buffer: UnsafeMutablePointer<UInt8>?, _ capacity: Int32) -> Int32 {
        lock.lock()
        defer { lock.unlock() }
        if let buffer = buffer, json.count <= Int(capacity) {
            for index in 0..<json.count { buffer[index] = json[index] }
        }
        return Int32(json.count)
    }

    // ---- RoomCaptureSessionDelegate -------------------------------------------------------------------

    func captureSession(_ session: RoomCaptureSession, didUpdate room: CapturedRoom) {
        guard session === current() else { return }
        let surfaces = (AtlasRoomPlanBridge.list(room.walls), AtlasRoomPlanBridge.list(room.doors),
                        AtlasRoomPlanBridge.list(room.windows), AtlasRoomPlanBridge.list(room.openings))
        let items = AtlasRoomPlanBridge.objectList(room.objects)
        let summary = AtlasRoomPlanBridge.summary(room)
        lock.lock()
        walls = surfaces.0; doors = surfaces.1; windows = surfaces.2; openings = surfaces.3
        objects = items
        rebuild()
        let changed = summary != lastSummary
        lastSummary = summary
        lock.unlock()
        // For spike S1: one line whenever the counts or the attribute strings change.
        if changed { AtlasRoomPlanBridge.log("Atlas RoomPlan: didUpdate " + summary) }
    }

    func captureSession(_ session: RoomCaptureSession, didProvide instruction: RoomCaptureSession.Instruction) {
        guard session === current() else { return }
        let name = AtlasRoomPlanBridge.name(of: instruction)
        lock.lock()
        let changed = name != self.instruction
        self.instruction = name
        rebuild()
        lock.unlock()
        if changed { AtlasRoomPlanBridge.log("Atlas RoomPlan: instruction " + name) }
    }

    func captureSession(_ session: RoomCaptureSession, didEndWith data: CapturedRoomData, error: Error?) {
        lock.lock()
        let stopped = session === ending
        let running = session === capture
        if stopped { ending = nil }
        lock.unlock()
        if running {
            // RoomPlan ended the capture by itself: what it saw so far stays readable until the scan stops.
            AtlasRoomPlanBridge.log("Atlas RoomPlan: capture ended by RoomPlan" + (error.map { ": \($0)" } ?? ""))
            return
        }
        guard stopped else { return }
        session.delegate = nil
        if let error = error {
            AtlasRoomPlanBridge.log("Atlas RoomPlan: capture ended with an error: \(error)")
            return
        }
        Task.detached(priority: .background) {
            do {
                let built = try await RoomBuilder(options: [.beautifyObjects]).capturedRoom(from: data)
                AtlasRoomPlanBridge.log("Atlas RoomPlan: RoomBuilder " + AtlasRoomPlanBridge.summary(built))
            } catch {
                AtlasRoomPlanBridge.log("Atlas RoomPlan: RoomBuilder failed: \(error)")
            }
        }
    }

    // NSLog, not print: it reaches the device console (Xcode's device logs) without a debugger attached.
    static func log(_ text: String) {
        NSLog("%@", text)
    }

    // ---- the file -------------------------------------------------------------------------------------

    private func current() -> RoomCaptureSession? {
        lock.lock()
        defer { lock.unlock() }
        return capture
    }

    // Under the lock.
    private func rebuild() {
        let text = "{\"schema\":\"atlas.room/1\",\"walls\":" + walls + ",\"doors\":" + doors +
            ",\"windows\":" + windows + ",\"openings\":" + openings + ",\"objects\":" + objects +
            ",\"instruction\":\"" + instruction + "\"}"
        json = [UInt8](text.utf8)
    }

    private static func list(_ surfaces: [CapturedRoom.Surface]) -> String {
        return "[" + surfaces.map { surface -> String in
            let centre = surface.transform.columns.3
            let along = surface.transform.columns.0
            return "{\"c\":" + vector(centre.x, centre.y, centre.z) + ",\"x\":" + vector(along.x, along.y, along.z) +
                ",\"w\":" + number(surface.dimensions.x) + ",\"h\":" + number(surface.dimensions.y) + "}"
        }.joined(separator: ",") + "]"
    }

    private static func objectList(_ objects: [CapturedRoom.Object]) -> String {
        return "[" + objects.map { object -> String in
            let t = object.transform
            let d = object.dimensions
            return "{\"id\":" + quoted(object.identifier.uuidString) +
                ",\"cat\":" + quoted(String(describing: object.category)) +
                ",\"c\":" + vector(t.columns.3.x, t.columns.3.y, t.columns.3.z) +
                ",\"x\":" + vector(t.columns.0.x, t.columns.0.y, t.columns.0.z) +
                ",\"z\":" + vector(t.columns.2.x, t.columns.2.y, t.columns.2.z) +
                ",\"d\":" + vector(d.x, d.y, d.z) +
                ",\"conf\":" + quoted(name(of: object.confidence)) +
                ",\"attrs\":[" + attributes(object).map { quoted($0) }.joined(separator: ",") + "]}"
        }.joined(separator: ",") + "]"
    }

    private static func attributes(_ object: CapturedRoom.Object) -> [String] {
        return object.attributes.map { $0.rawValue }
    }

    // One line for the S1 log: counts, then each object's category and attributes in every spelling RoomPlan
    // offers (raw value | short identifier | description), so the tags can be chosen from what the phone says.
    private static func summary(_ room: CapturedRoom) -> String {
        var text = "walls \(room.walls.count), doors \(room.doors.count), windows \(room.windows.count), " +
            "openings \(room.openings.count), objects \(room.objects.count)"
        for object in room.objects {
            let spelled = object.attributes.map { attribute -> String in
                attribute.rawValue + " | " + attribute.shortIdentifier + " | " + String(describing: attribute)
            }
            text += "; " + String(describing: object.category) + " [" + spelled.joined(separator: ", ") + "]"
        }
        return text
    }

    private static func name(of instruction: RoomCaptureSession.Instruction) -> String {
        switch instruction {
        case .moveCloseToWall: return "moveCloseToWall"
        case .moveAwayFromWall: return "moveAwayFromWall"
        case .slowDown: return "slowDown"
        case .turnOnLight: return "turnOnLight"
        case .lowTexture: return "lowTexture"
        case .normal: return "normal"
        @unknown default: return "normal"
        }
    }

    private static func name(of confidence: CapturedRoom.Confidence) -> String {
        switch confidence {
        case .low: return "low"
        case .medium: return "medium"
        case .high: return "high"
        @unknown default: return "low"
        }
    }

    private static func vector(_ x: Float, _ y: Float, _ z: Float) -> String {
        return "[" + number(x) + "," + number(y) + "," + number(z) + "]"
    }

    // JSON has no word for a number that is not finite: 0 makes the reader leave the surface or object out.
    private static func number(_ value: Float) -> String {
        return value.isFinite ? "\(value)" : "0"
    }

    // A JSON string: quotes, backslashes and control characters escaped.
    private static func quoted(_ text: String) -> String {
        var out = "\""
        for scalar in text.unicodeScalars {
            switch scalar {
            case "\"": out += "\\\""
            case "\\": out += "\\\\"
            case "\n": out += "\\n"
            case "\r": out += "\\r"
            case "\t": out += "\\t"
            default:
                if scalar.value < 0x20 {
                    out += String(format: "\\u%04x", scalar.value)
                } else {
                    out.unicodeScalars.append(scalar)
                }
            }
        }
        return out + "\""
    }
}

// 1 where RoomPlan can capture: an iPhone or iPad with LiDAR.
@_cdecl("AtlasRoomPlanSupported")
public func AtlasRoomPlanSupported() -> Int32 {
    return RoomCaptureSession.isSupported ? 1 : 0
}

// arSession is the ARSession* of Unity's session (UnityXRNativeSession.sessionPtr). 1 when the capture runs.
@_cdecl("AtlasRoomPlanStart")
public func AtlasRoomPlanStart(_ arSession: UnsafeRawPointer?) -> Int32 {
    guard let pointer = arSession else { return 0 }
    let session = Unmanaged<ARSession>.fromOpaque(pointer).takeUnretainedValue()
    return AtlasRoomPlanBridge.shared.start(session) ? 1 : 0
}

// Copies the file as UTF-8, with no terminator, when it fits in capacity bytes. Returns its length either
// way; 0 before the first update.
@_cdecl("AtlasRoomPlanCopyJson")
public func AtlasRoomPlanCopyJson(_ buffer: UnsafeMutablePointer<UInt8>?, _ capacity: Int32) -> Int32 {
    return AtlasRoomPlanBridge.shared.copyJson(buffer, capacity)
}

// Ends the capture; the ARSession keeps running. The file stays until the next start.
@_cdecl("AtlasRoomPlanStop")
public func AtlasRoomPlanStop() {
    AtlasRoomPlanBridge.shared.stop()
}
