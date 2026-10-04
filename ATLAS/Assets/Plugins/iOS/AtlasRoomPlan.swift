import ARKit
import Foundation
import RoomPlan

// Scan route 4, the wall bridge (Master Plan section 4.2, task P1-C05): walls, doors, windows and openings
// from RoomPlan, which Unity's AR Foundation does not pass on (it gives furniture boxes only).
// A RoomCaptureSession runs on Unity's own ARSession. Every update of the captured room is kept as one small
// JSON file, which the C# side (Atlas.Scan.ARKit.ArKitWallBridge) copies out and Atlas.World.WallBridgeJson
// reads:
//   {"schema":"atlas.walls/1","walls":[S,...],"doors":[S,...],"windows":[S,...],"openings":[S,...]}
//   S = {"c":[x,y,z],"x":[x,y,z],"w":width,"h":height}
// c is the centre of the surface and x the direction of its width (columns 3 and 0 of its transform), in
// ARKit's world frame; w and h are its dimensions, metres. The reader turns the frame into Unity's.
// The four C functions are called on the main thread. RoomPlan's updates may come on another, hence the lock.
// The project targets iOS 17, which RoomCaptureSession(arSession:) and stop(pauseARSession:) need.

final class AtlasRoomPlanBridge: NSObject, RoomCaptureSessionDelegate {
    static let shared = AtlasRoomPlanBridge()

    private let lock = NSLock()
    private var json = [UInt8]()
    private var capture: RoomCaptureSession?

    func start(_ arSession: ARSession) -> Bool {
        if capture != nil { return true }
        guard RoomCaptureSession.isSupported else { return false }
        lock.lock()
        json.removeAll()
        lock.unlock()
        let session = RoomCaptureSession(arSession: arSession)
        session.delegate = self
        session.run(configuration: RoomCaptureSession.Configuration())
        capture = session
        return true
    }

    func stop() {
        guard let session = capture else { return }
        capture = nil
        session.delegate = nil
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
    }

    func copyJson(_ buffer: UnsafeMutablePointer<UInt8>?, _ capacity: Int32) -> Int32 {
        lock.lock()
        defer { lock.unlock() }
        if let buffer = buffer, json.count <= Int(capacity) {
            for index in 0..<json.count { buffer[index] = json[index] }
        }
        return Int32(json.count)
    }

    func captureSession(_ session: RoomCaptureSession, didUpdate room: CapturedRoom) {
        let text = "{\"schema\":\"atlas.walls/1\",\"walls\":" + AtlasRoomPlanBridge.list(room.walls) +
            ",\"doors\":" + AtlasRoomPlanBridge.list(room.doors) +
            ",\"windows\":" + AtlasRoomPlanBridge.list(room.windows) +
            ",\"openings\":" + AtlasRoomPlanBridge.list(room.openings) + "}"
        let bytes = [UInt8](text.utf8)
        lock.lock()
        json = bytes
        lock.unlock()
    }

    private static func list(_ surfaces: [CapturedRoom.Surface]) -> String {
        return "[" + surfaces.map { surface -> String in
            let centre = surface.transform.columns.3
            let along = surface.transform.columns.0
            return "{\"c\":[" + number(centre.x) + "," + number(centre.y) + "," + number(centre.z) +
                "],\"x\":[" + number(along.x) + "," + number(along.y) + "," + number(along.z) +
                "],\"w\":" + number(surface.dimensions.x) + ",\"h\":" + number(surface.dimensions.y) + "}"
        }.joined(separator: ",") + "]"
    }

    // JSON has no word for a number that is not finite: 0 makes the reader leave the surface out.
    private static func number(_ value: Float) -> String {
        return value.isFinite ? "\(value)" : "0"
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
