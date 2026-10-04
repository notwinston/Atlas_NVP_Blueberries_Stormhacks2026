import ARKit
import CoreVideo
import Foundation
import QuartzCore
import Vision

// Tracked hands, the native detector (documents/ATLAS_Hands_Design.md sections 4.1 and 4.2, Appendix C):
// Apple Vision's hand-pose request on the camera frames ARKit already produces, with the LiDAR depth of the
// same frame sampled under each joint and over the palm. The C# side is Atlas.Hands.ARKit.VisionHandsSource.
//
// Pull only. Unity calls atlas_hands_tick and atlas_hands_poll once a frame on the main thread and neither
// waits for anything: a tick hands the newest ARFrame to the plugin's own queue when a detection is due, a
// poll copies out the newest finished result. The plugin reads Unity's ARSession and never configures, runs
// or pauses it, and it asks ARKit for no feature. Nothing happens between ticks, so while Unity is paused
// Vision does not run.
//
// No picture leaves a request (plan rule 10). The frame, its pixel buffer and its depth buffers are let go
// before the request's block returns; the result holds numbers only; no call returns pixels.
//
// The record atlas_hands_poll copies out is a byte layout, not a struct: Swift cannot declare a C struct and
// the framework target takes no bridging header. Each field is stored at its offset below, and the C# side is
// the very memory it is copied into: HandsDetection and DetectedHand in
// Assets/Atlas/Hands/Core/HandsDetection.cs. Atlas.Hands.Tests.HandsNativeLayoutTests reads this table and
// the constants of the two enums under it and compares both with those structs field by field, so the two
// sides cannot drift apart unnoticed. Little-endian, no padding anywhere.
//
//   record result 2712
//        0  Double     captureTime           the frame's timestamp, seconds
//        8  Double     pollTime              CACurrentMediaTime() when the result was copied out
//       16  Int32      version               1
//       20  Int32      sequence              +1 for every result; never starts again while the app lives
//       24  Int32      handCount             0, 1 or 2
//       28  Int32      imageWidth            the captured picture, pixels
//       32  Int32      imageHeight
//       36  Int32      depthWidth            the depth map; 0 when the frame had none
//       40  Int32      depthHeight
//       44  Int32      skipped               ticks that found the last request still running, since the last result
//       48  Float      fx                    intrinsics of the captured picture, pixels
//       52  Float      fy
//       56  Float      cx
//       60  Float      cy
//       64  Float      requestMs             how long Vision took; 0 when it was not asked
//       68  Float      reserved              0
//       72  Float[16]  cameraToSession       the frame's camera transform as ARKit gives it, column by column
//      136  hand       hand0
//     1424  hand       hand1
//
//   record hand 1288
//        0  Int32      side                  0 left, 1 right, -1 unknown
//        4  Float      confidence            Vision's, for the hand
//        8  Int32      palmCount             palm samples in use, 0 to 64
//       12  Int32      reserved              0
//       16  Float[64]  palmU                 picture pixels
//      272  Float[64]  palmV
//      528  Float[64]  palmDepth             metres, high confidence only
//      784  Float[21]  jointX                picture pixels from the top-left corner, in the order of HandJoint
//      868  Float[21]  jointY
//      952  Float[21]  jointConfidence       0 when Vision did not find the joint
//     1036  Float[21]  jointDepth            metres: median of the 3 x 3 depth pixels at the joint; 0 when none
//     1120  Float[21]  jointDepthSpread      metres: largest minus smallest of those pixels
//     1204  Int32[21]  jointDepthConfidence  the lowest among them: 0 low, 1 medium, 2 high; -1 no sample
//
// Thread use. The exported functions are called on the main thread only. A request runs on the plugin's
// serial queue. What both touch is under one lock, which the queue holds only for a few stores and a swap of
// two pointers. tick and poll try the lock and go on without it when it is taken (a tick takes it outright
// only once it knows that no request is running), so the main thread never waits for the detector. start and
// stop take it outright: they are rare, and they return at once.

// Byte offsets inside the result (the first table above).
enum AtlasHandsResultLayout {
    static let size = 2712
    static let captureTime = 0
    static let pollTime = 8
    static let version = 16
    static let sequence = 20
    static let handCount = 24
    static let imageWidth = 28
    static let imageHeight = 32
    static let depthWidth = 36
    static let depthHeight = 40
    static let skipped = 44
    static let fx = 48
    static let fy = 52
    static let cx = 56
    static let cy = 60
    static let requestMs = 64
    static let reserved = 68
    static let cameraToSession = 72
    static let hand0 = 136
    static let hand1 = 1424
}

// Byte offsets inside one hand (the second table above).
enum AtlasHandsHandLayout {
    static let size = 1288
    static let side = 0
    static let confidence = 4
    static let palmCount = 8
    static let reserved = 12
    static let palmU = 16
    static let palmV = 272
    static let palmDepth = 528
    static let jointX = 784
    static let jointY = 868
    static let jointConfidence = 952
    static let jointDepth = 1036
    static let jointDepthSpread = 1120
    static let jointDepthConfidence = 1204
}

// One field of the record, stored at its offset.
private extension UnsafeMutableRawPointer {
    func put(_ value: Double, at offset: Int) { storeBytes(of: value, toByteOffset: offset, as: Double.self) }
    func put(_ value: Int32, at offset: Int) { storeBytes(of: value, toByteOffset: offset, as: Int32.self) }
    func put(_ value: Float, at offset: Int) { storeBytes(of: value, toByteOffset: offset, as: Float.self) }
}

// The depth image of one frame while its buffers are locked: where a picture pixel falls in it, and the two
// ways the detector samples it. It holds the buffers' own memory and nothing else, and it is gone before the
// buffers are unlocked.
struct AtlasHandsDepthView {
    static let palmCapacity = 64
    // The most depth pixels of the palm's box that are looked at.
    static let palmCandidates = 4096
    // The palm's polygon is shrunk 15 % towards its centre before it is sampled, which keeps the samples off
    // the hand's outline, where a LiDAR pixel mixes the hand with what is behind it.
    static let palmKeep: Float = 0.85
    // ARConfidenceLevel.high.
    static let high: Int32 = 2

    let depth: UnsafeRawPointer          // Float32 metres, rows of depthRowBytes
    let depthRowBytes: Int
    let confidence: UnsafeRawPointer?    // UInt8 per pixel; nil when the frame has no confidence map
    let confidenceRowBytes: Int
    let width: Int
    let height: Int
    // depth pixel = picture pixel * scale + offset, both counted from the top-left corner.
    let scaleX: Float
    let scaleY: Float
    let offsetX: Float
    let offsetY: Float
    // The picture and the depth map have different shapes (see init).
    let centred: Bool

    init(depth: UnsafeRawPointer, depthRowBytes: Int, confidence: UnsafeRawPointer?, confidenceRowBytes: Int,
         width: Int, height: Int, imageWidth: Int, imageHeight: Int) {
        self.depth = depth
        self.depthRowBytes = depthRowBytes
        self.confidence = confidence
        self.confidenceRowBytes = confidenceRowBytes
        self.width = width
        self.height = height
        let ratioX = Float(width) / Float(imageWidth)
        let ratioY = Float(height) / Float(imageHeight)
        if abs(ratioX - ratioY) <= 0.02 * max(ratioX, ratioY) {
            // The same shape: the depth map covers the picture, and a pixel maps by the ratio of the two sizes.
            scaleX = ratioX
            scaleY = ratioY
            offsetX = 0
            offsetY = 0
            centred = false
        } else {
            // Different shapes, for example a 16:9 picture (1280 x 720) with a 4:3 depth map (256 x 192).
            // ASSUMED, never seen on a phone: both show the same width of the scene with square pixels, so
            // one scale, the ratio of the two widths, serves both directions, and the picture's rows sit in
            // the middle of the depth map's (in the example the depth map shows 24 rows more above the
            // picture and 24 below). Apple states that the depth map keeps the picture's aspect ratio
            // (256 x 144 for 16:9), in which case this branch never runs. THE BENCH MUST CHECK which of the
            // two a phone delivers (the first frame's line in the device log says), and, if it is this
            // branch, that a finger held at the top edge and at the bottom edge of the picture reads the
            // finger's depth and not the depth of what is behind it.
            scaleX = ratioX
            scaleY = ratioX
            offsetX = 0
            offsetY = (Float(height) - Float(imageHeight) * ratioX) / 2
            centred = true
        }
    }

    // Metres at a depth pixel, 0 for no return.
    func metres(_ x: Int, _ y: Int) -> Float {
        let value = depth.loadUnaligned(fromByteOffset: y * depthRowBytes + x * 4, as: Float.self)
        return value.isFinite && value > 0 ? value : 0
    }

    // ARKit's confidence at a depth pixel: 0 low, 1 medium, 2 high. Low when the frame has no confidence map:
    // depth that nothing vouches for is not trusted.
    func level(_ x: Int, _ y: Int) -> Int32 {
        guard let confidence = confidence else { return 0 }
        return Int32(min(confidence.load(fromByteOffset: y * confidenceRowBytes + x, as: UInt8.self), 2))
    }

    // The depth under one joint: the median of the returns among the 3 x 3 depth pixels around it, the largest
    // minus the smallest of them (large where the window straddles the hand's outline) and the lowest
    // confidence among them. (0, 0, -1) when the joint is off the depth map or none of the nine is a return.
    // window is room for nine values.
    func sampleJoint(pixelX: Float, pixelY: Float,
                     window: UnsafeMutablePointer<Float>) -> (depth: Float, spread: Float, confidence: Int32) {
        let mapX = pixelX * scaleX + offsetX
        let mapY = pixelY * scaleY + offsetY
        // False for a value that is not a number too, which the conversions below would trap on.
        guard mapX >= 0, mapY >= 0, mapX < Float(width), mapY < Float(height) else { return (0, 0, -1) }
        let centreX = Int(mapX)
        let centreY = Int(mapY)
        var count = 0
        var lowest = AtlasHandsDepthView.high
        for y in (centreY - 1)...(centreY + 1) where y >= 0 && y < height {
            for x in (centreX - 1)...(centreX + 1) where x >= 0 && x < width {
                let value = metres(x, y)
                if value <= 0 { continue }
                // Kept in order as they come: at most nine.
                var slot = count
                while slot > 0 && window[slot - 1] > value {
                    window[slot] = window[slot - 1]
                    slot -= 1
                }
                window[slot] = value
                count += 1
                lowest = min(lowest, level(x, y))
            }
        }
        if count == 0 { return (0, 0, -1) }
        return (window[count / 2], window[count - 1] - window[0], lowest)
    }

    // Up to 64 depth samples spread over the palm, stored in one hand of the record; returns how many.
    // corners are the wrist, the thumb's base, the index knuckle and the little-finger knuckle, in that order
    // (a quadrilateral that does not cross itself), in picture pixels. A sample is the centre of a depth pixel,
    // given back in picture pixels with that pixel's depth. Only returns of high confidence are kept, which is
    // all the palm's plane fit may use; when there are more than 64 of them an even share is taken, so the
    // samples cover the whole palm and are not the first 64 from its top.
    func samplePalm(cornersX: SIMD4<Float>, cornersY: SIMD4<Float>, into hand: UnsafeMutableRawPointer) -> Int32 {
        let keep = AtlasHandsDepthView.palmKeep
        let capacity = AtlasHandsDepthView.palmCapacity
        let centreX = cornersX.sum() / 4
        let centreY = cornersY.sum() / 4
        // The shrunk polygon in depth pixels, and its box inside the map.
        let polygonX = (centreX + (cornersX - centreX) * keep) * scaleX + offsetX
        let polygonY = (centreY + (cornersY - centreY) * keep) * scaleY + offsetY
        let left = max(polygonX.min(), 0)
        let right = min(polygonX.max(), Float(width))
        let top = max(polygonY.min(), 0)
        let bottom = min(polygonY.max(), Float(height))
        // False for a value that is not a number too, which the conversions below would trap on.
        guard right > left, bottom > top else { return 0 }
        let firstX = Int(left)
        let lastX = min(Int(right), width - 1)
        let firstY = Int(top)
        let lastY = min(Int(bottom), height - 1)
        // A palm that fills the view is walked every second or third pixel: a few thousand candidates are plenty.
        let boxPixels = (lastX - firstX + 1) * (lastY - firstY + 1)
        let stride = max(1, Int((Float(boxPixels) / Float(AtlasHandsDepthView.palmCandidates)).squareRoot().rounded(.up)))

        // Walks the box in reading order over the pixels whose centre is inside the polygon and which are
        // returns of high confidence. With hand nil it counts them. Otherwise total is that count, and the
        // pixels at which a new sixty-fourth of the count begins are stored: all of them when there are 64 or
        // fewer, an even share of exactly 64 otherwise.
        func walk(of total: Int, into hand: UnsafeMutableRawPointer?) -> Int {
            var index = 0
            var stored = 0
            var y = firstY
            while y <= lastY {
                var x = firstX
                while x <= lastX {
                    if AtlasHandsDepthView.inside(Float(x) + 0.5, Float(y) + 0.5, polygonX, polygonY) {
                        let value = metres(x, y)
                        if value > 0 && level(x, y) == AtlasHandsDepthView.high {
                            if let hand = hand, stored < capacity,
                               (index + 1) * capacity / total > index * capacity / total {
                                hand.put((Float(x) + 0.5 - offsetX) / scaleX, at: AtlasHandsHandLayout.palmU + stored * 4)
                                hand.put((Float(y) + 0.5 - offsetY) / scaleY, at: AtlasHandsHandLayout.palmV + stored * 4)
                                hand.put(value, at: AtlasHandsHandLayout.palmDepth + stored * 4)
                                stored += 1
                            }
                            index += 1
                        }
                    }
                    x += stride
                }
                y += stride
            }
            return hand == nil ? index : stored
        }

        let total = walk(of: 0, into: nil)
        guard total > 0 else { return 0 }
        return Int32(walk(of: total, into: hand))
    }

    // Even-odd rule: true when the point is inside the quadrilateral, whichever way round its corners go.
    private static func inside(_ x: Float, _ y: Float, _ polygonX: SIMD4<Float>, _ polygonY: SIMD4<Float>) -> Bool {
        var inside = false
        var previous = 3
        for corner in 0..<4 {
            if (polygonY[corner] > y) != (polygonY[previous] > y) {
                let crossing = polygonX[previous] + (y - polygonY[previous]) / (polygonY[corner] - polygonY[previous]) *
                    (polygonX[corner] - polygonX[previous])
                if x < crossing { inside = !inside }
            }
            previous = corner
        }
        return inside
    }
}

// The detector. Its state belongs to three owners, marked below: the main thread, the detector queue and the
// lock. The compiler cannot check that division, hence unchecked.
final class AtlasHandsDetector: @unchecked Sendable {
    static let shared = AtlasHandsDetector()

    // What the exported functions answer with when something is wrong (Appendix C).
    static let notStarted: Int32 = -1
    static let noSession: Int32 = -2
    static let noFrame: Int32 = -3
    static let requestFailed: Int32 = -4

    private static let recordVersion: Int32 = 1
    private static let maxHands = 2
    private static let jointCount = 21
    // UnityXRNativeSession_1.version in the ARKit package's UnityXRNativePtrs.h.
    private static let nativeSessionVersion: Int32 = 1

    // The app is fixed to Unity's LandscapeLeft, which is iOS's interface landscapeRight and the camera
    // sensor's own orientation: the captured picture is upright as it is stored, and ARKit's camera space is
    // defined in that orientation. Any other value would turn the hands and swap left and right.
    // THE BENCH MUST CHECK it with a hand in the top-left quarter of the view: its joints must come back with
    // x under half the picture's width and y under half its height, and a right hand must be called right.
    private static let orientation = CGImagePropertyOrientation.up

    // A detection starts this much before a full period has passed since the last one: half a display frame,
    // so that at 60 frames a second 30 Hz is every second frame, 15 Hz every fourth and 5 Hz every twelfth,
    // whatever the jitter of the frame times.
    private static let rateSlack: CFTimeInterval = 1.0 / 120.0
    // The most one tick adds to the time without a frame (see tick).
    private static let longestTickStep: CFTimeInterval = 0.1
    // This long without a new frame, in ticks' time, is error -3.
    private static let noFrameSeconds: CFTimeInterval = 2.0
    // A frame older than this when a tick finds it is left over from before a pause. The hands code uses no
    // result older than 0.2 s, so a request on it would be wasted.
    private static let staleFrameSeconds: CFTimeInterval = 0.5
    // This many failed requests in a row are error -4, and the request object is built afresh. One failed
    // request is only counted: Vision can fail once when the app leaves the foreground.
    private static let failuresBeforeFault: Int32 = 30
    // The palm is sampled only when each of its four corners is at least this sure (the lift's own floor for a
    // joint, HandsConstants.JointConfidenceFloor): a corner that is a guess would put samples on the background.
    private static let palmCornerConfidence: Float = 0.3

    // In the order of Atlas.World.HandJoint: the wrist, then each finger from its base to its tip.
    private static let jointNames: [VNHumanHandPoseObservation.JointName] = [
        .wrist,
        .thumbCMC, .thumbMP, .thumbIP, .thumbTip,
        .indexMCP, .indexPIP, .indexDIP, .indexTip,
        .middleMCP, .middlePIP, .middleDIP, .middleTip,
        .ringMCP, .ringPIP, .ringDIP, .ringTip,
        .littleMCP, .littlePIP, .littleDIP, .littleTip,
    ]

    // One request at a time, in order. Each block's autoreleased objects go when it ends.
    private let queue = DispatchQueue(label: "atlas.hands.detect", qos: .userInitiated, autoreleaseFrequency: .workItem)
    private let lock = NSLock()

    // ---- Main thread only ----
    // Weak: the session is Unity's. One that is gone reads as nil and gives -2.
    private weak var session: ARSession?
    private var started = false
    // Seconds between detections; 0 idles.
    private var period: CFTimeInterval = 0
    private var lastAccept: CFTimeInterval = 0
    private var lastTick: CFTimeInterval = 0
    // Timestamp of the last frame handed to the queue.
    private var lastStamp: TimeInterval = -1
    // Ticks' time without a new frame.
    private var starved: CFTimeInterval = 0
    // 0, or the error the last tick found: -2 or -3.
    private var condition: Int32 = 0
    // What poll last saw under the lock, for the frame on which it cannot have the lock.
    private var failing = false

    // ---- Detector queue only ----
    // Built on the queue by the first request, so that nothing of Vision is touched on the main thread, and
    // used again by every request after it. Between two requests it holds the last one's joints and no
    // picture: the picture belongs to the request handler, which lives for one request.
    private var request: VNDetectHumanHandPoseRequest?
    // Nine depth values for one joint.
    private let window = UnsafeMutablePointer<Float>.allocate(capacity: 9)
    // The run whose first frame has been described in the device log.
    private var describedEpoch: UInt32 = 0
    // The record being written. Exchanged with front under the lock when it is finished.
    private var back = UnsafeMutableRawPointer.allocate(byteCount: AtlasHandsResultLayout.size, alignment: 8)

    // ---- Under the lock ----
    // The newest finished record.
    private var front = UnsafeMutableRawPointer.allocate(byteCount: AtlasHandsResultLayout.size, alignment: 8)
    // The frame a tick took, until the queue picks it up. Nil between requests.
    private var pending: ARFrame?
    private var busy = false
    // Raised by start and stop: a request from before either is finished and thrown away.
    private var generation: UInt32 = 0
    private var sequence: Int32 = 0
    private var delivered: Int32 = 0
    private var skipped: Int32 = 0
    // Failed requests in a row.
    private var failures: Int32 = 0

    private init() {
        back.initializeMemory(as: UInt8.self, repeating: 0, count: AtlasHandsResultLayout.size)
        front.initializeMemory(as: UInt8.self, repeating: 0, count: AtlasHandsResultLayout.size)
    }

    private static func makeRequest() -> VNDetectHumanHandPoseRequest {
        let request = VNDetectHumanHandPoseRequest()
        request.maximumHandCount = maxHands
        return request
    }

    private static func log(_ text: String) {
        NSLog("%@", ("AtlasHands: " + text) as NSString)
    }

    // ---- Main thread ----

    func start(_ nativeSession: UnsafeRawPointer?, _ hz: Int32) -> Int32 {
        // What AR Foundation hands out as XRSessionSubsystem.nativePtr:
        //   struct UnityXRNativeSession_1 { int version; void* sessionPtr; }
        // The pointer follows the version at the next pointer boundary. It is read here, once per start, and
        // not kept: the ARSession it names is.
        let pointerOffset = MemoryLayout<UnsafeRawPointer>.alignment
        guard let native = nativeSession,
              native.loadUnaligned(as: Int32.self) == AtlasHandsDetector.nativeSessionVersion,
              let pointer = native.loadUnaligned(fromByteOffset: pointerOffset, as: UnsafeRawPointer?.self) else {
            return AtlasHandsDetector.noSession
        }
        session = Unmanaged<ARSession>.fromOpaque(pointer).takeUnretainedValue()
        started = true
        setRate(hz)
        lastAccept = 0
        lastTick = CACurrentMediaTime()
        lastStamp = -1
        starved = 0
        condition = 0
        failing = false
        discard()
        return 0
    }

    func setRate(_ hz: Int32) {
        period = hz > 0 ? 1.0 / Double(min(hz, 60)) : 0
    }

    // Returns at once: a request that is still running finishes by itself and its result is thrown away.
    func stop() {
        started = false
        session = nil
        period = 0
        condition = 0
        failing = false
        discard()
    }

    // Whatever is on its way belongs to the run that just ended: the result of a request still running is
    // thrown away when it arrives, and a result nobody polled is forgotten.
    private func discard() {
        lock.lock()
        generation &+= 1
        let waiting = pending
        pending = nil
        delivered = sequence
        skipped = 0
        failures = 0
        lock.unlock()
        // A frame the queue had not picked up is let go here, after the lock.
        withExtendedLifetime(waiting) {}
    }

    // 1 a request was started on the newest frame, 0 nothing to do, negative an error that holds now.
    func tick() -> Int32 {
        guard started else { return AtlasHandsDetector.notStarted }
        guard let session = session else {
            condition = AtlasHandsDetector.noSession
            return condition
        }
        let now = CACurrentMediaTime()
        // The time this tick stands for. Unity does not tick while the app is paused, and ARKit stops its
        // frames while the app is in the background, so a pause is one long gap between two ticks. It counts
        // as one step of at most 0.1 s: coming back from the background can never look like two seconds
        // without a frame.
        let step = min(max(now - lastTick, 0), AtlasHandsDetector.longestTickStep)
        lastTick = now
        guard period > 0 else {
            starved = 0
            condition = 0
            return 0
        }
        guard now - lastAccept >= period - AtlasHandsDetector.rateSlack else { return condition }

        // A detection is due. The last request still running: this tick is skipped and counted.
        guard lock.try() else { return condition }
        let running = busy
        if running { skipped &+= 1 }
        lock.unlock()
        if running { return condition }

        // ARSession is not Sendable and nothing documents its currentFrame for other threads: it is read
        // here, on the thread Unity's own ARKit provider reads it on. The pool makes sure the main thread
        // keeps no reference to the frame past this tick.
        var newestAge = CFTimeInterval.nan
        let taken: Bool = autoreleasepool {
            guard let frame = session.currentFrame else { return false }
            newestAge = now - frame.timestamp
            guard frame.timestamp != lastStamp, newestAge <= AtlasHandsDetector.staleFrameSeconds else { return false }
            lastStamp = frame.timestamp
            // Never waits: no request is running, so nothing else can hold the lock.
            lock.lock()
            pending = frame
            busy = true
            let epoch = generation
            lock.unlock()
            queue.async { [self] in
                autoreleasepool { self.run(epoch) }
            }
            return true
        }
        if taken {
            lastAccept = now
            starved = 0
            condition = 0
            return 1
        }

        starved += step
        if starved >= AtlasHandsDetector.noFrameSeconds && condition != AtlasHandsDetector.noFrame {
            condition = AtlasHandsDetector.noFrame
            // The age tells a camera that has stopped (it grows) from a clock that is not the frames' own
            // (it is far from zero from the start).
            AtlasHandsDetector.log("no new camera frame for 2 s; the newest one is " +
                (newestAge.isNaN ? "missing" : String(format: "%.3f s old", newestAge)))
        }
        return condition
    }

    // 1 the newest result was copied into out (2,712 bytes), 0 nothing new, negative an error that holds now.
    func poll(_ out: UnsafeMutableRawPointer?) -> Int32 {
        guard started else { return AtlasHandsDetector.notStarted }
        guard let out = out else { return 0 }
        var fresh = false
        // The queue has the lock for a moment while it publishes: then the result is taken on the next frame.
        if lock.try() {
            fresh = sequence != delivered
            if fresh {
                out.copyMemory(from: front, byteCount: AtlasHandsResultLayout.size)
                delivered = sequence
            }
            failing = failures >= AtlasHandsDetector.failuresBeforeFault
            lock.unlock()
        }
        if fresh {
            // ARFrame.timestamp is on this clock in practice (seconds since the device started); Apple does
            // not say so. THE BENCH MUST CHECK that pollTime - captureTime lies between 0.02 and 0.2 s.
            out.put(CACurrentMediaTime(), at: AtlasHandsResultLayout.pollTime)
            return 1
        }
        if session == nil { return AtlasHandsDetector.noSession }
        if failing { return AtlasHandsDetector.requestFailed }
        return condition
    }

    // ---- Detector queue ----

    private enum Outcome {
        case nothing            // stopped before the queue picked the frame up, or a frame without a picture
        case result             // the back buffer holds a finished record
        case failed(Error)      // Vision threw
    }

    private func run(_ epoch: UInt32) {
        let outcome: Outcome
        do {
            lock.lock()
            let frame = pending
            pending = nil
            lock.unlock()
            // The frame is this block's alone now. Its picture and its depth buffers are used inside detect and
            // nowhere else, and the frame is let go at the end of this scope, before the block returns.
            if let frame = frame {
                outcome = withExtendedLifetime(frame) { detect(frame, epoch) }
            } else {
                outcome = .nothing
            }
        }

        lock.lock()
        if epoch == generation {
            switch outcome {
            case .result:
                sequence &+= 1
                back.put(sequence, at: AtlasHandsResultLayout.sequence)
                back.put(skipped, at: AtlasHandsResultLayout.skipped)
                swap(&front, &back)
                skipped = 0
                failures = 0
            case .failed:
                failures &+= 1
            case .nothing:
                break
            }
        }
        let streak = failures
        busy = false
        lock.unlock()

        if case .failed(let error) = outcome {
            let threshold = AtlasHandsDetector.failuresBeforeFault
            if streak == 1 || streak % threshold == 0 {
                let failure = error as NSError
                AtlasHandsDetector.log("the hand-pose request failed (\(streak) in a row): " +
                    "\(failure.domain) \(failure.code) \(failure.localizedDescription)")
            }
            // The next request builds a new request object.
            if streak > 0 && streak % threshold == 0 { request = nil }
        }
    }

    // One frame into the back buffer. Everything that touches the picture or the depth image is in here.
    private func detect(_ frame: ARFrame, _ epoch: UInt32) -> Outcome {
        let image = frame.capturedImage
        let imageWidth = CVPixelBufferGetWidth(image)
        let imageHeight = CVPixelBufferGetHeight(image)
        guard imageWidth > 0, imageHeight > 0 else { return .nothing }
        let camera = frame.camera
        let request = self.request ?? AtlasHandsDetector.makeRequest()
        self.request = request

        // A hand is placed in the room with the camera pose of its own frame, so that pose must be one in the
        // session's frame of reference. It is not while ARKit has no pose at all (not available), is still
        // setting its frame up (initialising) or is finding its way back into a map (relocalising: the frame of
        // reference jumps when it has). The record has no field to say so. Such a frame therefore gives a result
        // with no hands, and Vision is not asked: the hands coast and fade as on any brief loss. While tracking
        // is limited by fast motion or by too little to see, the pose drifts but stays in the same frame, and
        // the hands are detected as usual; Bump Guard hides them once tracking has not been normal for half a second.
        var observations = [VNHumanHandPoseObservation]()
        var requestMs: Float = 0
        if AtlasHandsDetector.posePlacesHands(camera.trackingState) {
            let began = CACurrentMediaTime()
            let handler = VNImageRequestHandler(cvPixelBuffer: image, orientation: AtlasHandsDetector.orientation,
                                                options: [:])
            do {
                try handler.perform([request])
            } catch {
                return .failed(error)
            }
            requestMs = Float((CACurrentMediaTime() - began) * 1000)
            observations = request.results ?? []
        }

        // The depth of the same frame, read in place. The two buffers belong to the frame (ARDepthData does
        // not retain them for its readers), so they are used only inside this function, and unlocked on every
        // way out of it. A format other than the expected one counts as no depth.
        var lockedDepth: CVPixelBuffer?
        var lockedConfidence: CVPixelBuffer?
        defer {
            if let map = lockedConfidence { CVPixelBufferUnlockBaseAddress(map, .readOnly) }
            if let map = lockedDepth { CVPixelBufferUnlockBaseAddress(map, .readOnly) }
        }
        var view: AtlasHandsDepthView?
        if let scene = frame.sceneDepth {
            let map = scene.depthMap
            if CVPixelBufferGetPixelFormatType(map) == kCVPixelFormatType_DepthFloat32,
               CVPixelBufferLockBaseAddress(map, .readOnly) == kCVReturnSuccess {
                lockedDepth = map
                let width = CVPixelBufferGetWidth(map)
                let height = CVPixelBufferGetHeight(map)
                let rowBytes = CVPixelBufferGetBytesPerRow(map)
                if let base = CVPixelBufferGetBaseAddress(map), width > 0, height > 0, rowBytes >= width * 4 {
                    var levels: UnsafeMutableRawPointer?
                    var levelsRowBytes = 0
                    if let confidence = scene.confidenceMap,
                       CVPixelBufferGetPixelFormatType(confidence) == kCVPixelFormatType_OneComponent8,
                       CVPixelBufferGetWidth(confidence) == width, CVPixelBufferGetHeight(confidence) == height,
                       CVPixelBufferLockBaseAddress(confidence, .readOnly) == kCVReturnSuccess {
                        lockedConfidence = confidence
                        levelsRowBytes = CVPixelBufferGetBytesPerRow(confidence)
                        if levelsRowBytes >= width { levels = CVPixelBufferGetBaseAddress(confidence) }
                    }
                    view = AtlasHandsDepthView(depth: UnsafeRawPointer(base), depthRowBytes: rowBytes,
                                               confidence: levels.map { UnsafeRawPointer($0) },
                                               confidenceRowBytes: levelsRowBytes, width: width, height: height,
                                               imageWidth: imageWidth, imageHeight: imageHeight)
                }
            }
        }

        // The record, from zero: what is not stored below stays 0.
        let record = back
        record.initializeMemory(as: UInt8.self, repeating: 0, count: AtlasHandsResultLayout.size)
        record.put(frame.timestamp, at: AtlasHandsResultLayout.captureTime)
        record.put(AtlasHandsDetector.recordVersion, at: AtlasHandsResultLayout.version)
        record.put(Int32(imageWidth), at: AtlasHandsResultLayout.imageWidth)
        record.put(Int32(imageHeight), at: AtlasHandsResultLayout.imageHeight)
        record.put(Int32(view?.width ?? 0), at: AtlasHandsResultLayout.depthWidth)
        record.put(Int32(view?.height ?? 0), at: AtlasHandsResultLayout.depthHeight)
        // simd matrices are stored by column: the focal lengths are on the diagonal, the principal point is
        // the third column. ARKit's header counts the principal point from the centre of the top-left pixel,
        // its documentation from the corner; the half pixel between them is a quarter of a millimetre at arm's
        // length, and the value is passed on as it is, as ArDepthSource does for Bump Guard's depth.
        let intrinsics = camera.intrinsics
        record.put(intrinsics[0][0], at: AtlasHandsResultLayout.fx)
        record.put(intrinsics[1][1], at: AtlasHandsResultLayout.fy)
        record.put(intrinsics[2][0], at: AtlasHandsResultLayout.cx)
        record.put(intrinsics[2][1], at: AtlasHandsResultLayout.cy)
        record.put(requestMs, at: AtlasHandsResultLayout.requestMs)
        // ARKit's own convention (right-handed, the camera looking along -z), column by column. C# turns it
        // into Unity's (Atlas.Hands.HandsNativeMath.ArKitToUnity).
        let transform = camera.transform
        for column in 0..<4 {
            for row in 0..<4 {
                record.put(transform[column][row], at: AtlasHandsResultLayout.cameraToSession + (column * 4 + row) * 4)
            }
        }

        // Vision orders hands by size and gives the largest: with at most two asked for, those are the walker's.
        let handCount = min(observations.count, AtlasHandsDetector.maxHands)
        record.put(Int32(handCount), at: AtlasHandsResultLayout.handCount)
        let names = AtlasHandsDetector.jointNames
        for index in 0..<handCount {
            let observation = observations[index]
            let hand = record + (index == 0 ? AtlasHandsResultLayout.hand0 : AtlasHandsResultLayout.hand1)
            // Vision's own numbers are left -1, unknown 0, right +1: never stored as they are.
            let side: Int32
            switch observation.chirality {
            case .left: side = 0
            case .right: side = 1
            case .unknown: side = -1
            }
            hand.put(side, at: AtlasHandsHandLayout.side)
            hand.put(Float(observation.confidence), at: AtlasHandsHandLayout.confidence)

            var cornersX = SIMD4<Float>(repeating: 0)
            var cornersY = SIMD4<Float>(repeating: 0)
            var corners = 0
            for joint in 0..<AtlasHandsDetector.jointCount {
                let slot = joint * 4
                hand.put(Int32(-1), at: AtlasHandsHandLayout.jointDepthConfidence + slot)
                guard let point = try? observation.recognizedPoint(names[joint]), point.confidence > 0 else { continue }
                // Vision: a share of the picture's width and height, from the bottom-left corner.
                // The record: pixels from the top-left corner.
                let x = Float(point.location.x) * Float(imageWidth)
                let y = (1 - Float(point.location.y)) * Float(imageHeight)
                guard x.isFinite, y.isFinite else { continue }
                hand.put(x, at: AtlasHandsHandLayout.jointX + slot)
                hand.put(y, at: AtlasHandsHandLayout.jointY + slot)
                hand.put(Float(point.confidence), at: AtlasHandsHandLayout.jointConfidence + slot)
                if let view = view {
                    let sample = view.sampleJoint(pixelX: x, pixelY: y, window: window)
                    hand.put(sample.depth, at: AtlasHandsHandLayout.jointDepth + slot)
                    hand.put(sample.spread, at: AtlasHandsHandLayout.jointDepthSpread + slot)
                    hand.put(sample.confidence, at: AtlasHandsHandLayout.jointDepthConfidence + slot)
                }
                if let corner = AtlasHandsDetector.palmCorner(joint),
                   point.confidence >= AtlasHandsDetector.palmCornerConfidence {
                    cornersX[corner] = x
                    cornersY[corner] = y
                    corners += 1
                }
            }
            if let view = view, corners == 4 {
                let palmCount = view.samplePalm(cornersX: cornersX, cornersY: cornersY, into: hand)
                hand.put(palmCount, at: AtlasHandsHandLayout.palmCount)
            }
        }

        // Once per start, what the bench needs to see before anything is built on the numbers.
        if describedEpoch != epoch {
            describedEpoch = epoch
            var depthText = "no depth"
            if let view = view {
                depthText = "depth \(view.width) x \(view.height), " +
                    (view.confidence != nil ? "with" : "without") + " confidence, " +
                    (view.centred ? "ANOTHER SHAPE than the picture: centred, unchecked" : "the picture's shape")
            }
            let format = CVPixelBufferGetPixelFormatType(image)
            let age = (CACurrentMediaTime() - frame.timestamp) * 1000
            AtlasHandsDetector.log("first frame: picture \(imageWidth) x \(imageHeight) format \(format), " + depthText +
                String(format: ", fx %.1f fy %.1f cx %.1f cy %.1f, ", intrinsics[0][0], intrinsics[1][1],
                       intrinsics[2][0], intrinsics[2][1]) +
                String(format: "%.1f ms old when its request ended, ", age) +
                "tracking \(AtlasHandsDetector.describe(camera.trackingState)), request revision \(request.revision), " +
                "\(handCount) hand(s) in " + String(format: "%.1f ms", requestMs))
        }
        return .result
    }

    // Whether a frame's camera pose is one in the session's frame of reference (see detect).
    private static func posePlacesHands(_ state: ARCamera.TrackingState) -> Bool {
        switch state {
        case .normal:
            return true
        case .limited(let reason):
            switch reason {
            case .excessiveMotion, .insufficientFeatures: return true
            case .initializing, .relocalizing: return false
            @unknown default: return false
            }
        case .notAvailable:
            return false
        @unknown default:
            return false
        }
    }

    private static func describe(_ state: ARCamera.TrackingState) -> String {
        switch state {
        case .normal:
            return "normal"
        case .limited(let reason):
            switch reason {
            case .excessiveMotion: return "limited by motion"
            case .insufficientFeatures: return "limited by too little to see"
            case .initializing: return "initialising (no hands)"
            case .relocalizing: return "relocalising (no hands)"
            @unknown default: return "limited (no hands)"
            }
        case .notAvailable:
            return "not available (no hands)"
        @unknown default:
            return "unknown (no hands)"
        }
    }

    // Where a joint stands in the palm's polygon: wrist, thumb's base, index knuckle, little-finger knuckle.
    private static func palmCorner(_ joint: Int) -> Int? {
        switch joint {
        case 0: return 0
        case 1: return 1
        case 5: return 2
        case 17: return 3
        default: return nil
        }
    }
}

// The six functions Atlas.Hands.ARKit.VisionHandsSource imports. Main thread only.

// session is XRSessionSubsystem.nativePtr of Unity's AR session: a struct of an Int32 version followed by the
// ARSession pointer. The session is only read. hz as atlas_hands_set_rate. 0 on success, -2 when there is no
// session to read (a null pointer, another version of the struct, no ARSession in it).
@_cdecl("atlas_hands_start")
public func atlas_hands_start(_ session: UnsafeRawPointer?, _ hz: Int32) -> Int32 {
    return AtlasHandsDetector.shared.start(session, hz)
}

// Detections per second: 30, 15 or 5. 0 idles: ticks do nothing and no frame is read.
@_cdecl("atlas_hands_set_rate")
public func atlas_hands_set_rate(_ hz: Int32) {
    AtlasHandsDetector.shared.setRate(hz)
}

// Once per Unity frame. When a detection is due at the current rate and no request is running, takes the
// session's current frame (unless it was already taken, or is left over from before a pause) and hands it to
// the plugin's queue. Returns at once: 1 a request was started, 0 nothing to do, -1 not started, -2 the
// session is gone, -3 no new frame for 2 s of ticks.
@_cdecl("atlas_hands_tick")
public func atlas_hands_tick() -> Int32 {
    return AtlasHandsDetector.shared.tick()
}

// 1 a result newer than the last one handed out was copied into out, which must have room for
// atlas_hands_result_size() bytes; 0 nothing new; -1 not started; -2 the session is gone; -3 no new frame for
// 2 s of ticks; -4 the last 30 requests all failed. An error is answered on every call while it holds and
// goes away by itself. Returns at once.
@_cdecl("atlas_hands_poll")
public func atlas_hands_poll(_ out: UnsafeMutableRawPointer?) -> Int32 {
    return AtlasHandsDetector.shared.poll(out)
}

// The size of the record in bytes: 2,712. C# refuses to run against a plugin that answers anything else.
@_cdecl("atlas_hands_result_size")
public func atlas_hands_result_size() -> Int32 {
    return Int32(AtlasHandsResultLayout.size)
}

// Stops ticking and forgets the session and any result not yet polled. Returns at once.
@_cdecl("atlas_hands_stop")
public func atlas_hands_stop() {
    AtlasHandsDetector.shared.stop()
}
