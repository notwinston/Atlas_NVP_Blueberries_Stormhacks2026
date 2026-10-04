#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <CoreHaptics/CoreHaptics.h>
#include <atomic>

// Haptic pulses for the Bump Guard warning cue (Master Plan section 4.10, task P1-C14).
// CoreHaptics plays one transient per call; when the engine cannot be had, UIKit's impact generators stand in.
// The C# side (Atlas.Safety.IosHaptics) times the pulses: 2 per second for WARN, 6 per second for STRONG.
// Every function is called on the main thread. The engine's handlers run on other threads and only set a flag.
// Needs CoreHaptics.framework in the UnityFramework target.

#if __has_feature(objc_arc)
#define ATLAS_AUTORELEASE(x) (x)
#else
#define ATLAS_AUTORELEASE(x) [(x) autorelease]
#endif

static CHHapticEngine* sEngine = nil;                     // kept for the life of the app
static std::atomic<bool> sEngineRunning(false);
static UIImpactFeedbackGenerator* sMedium = nil;
static UIImpactFeedbackGenerator* sHeavy = nil;

// Starts the engine if it is not running. The system stops it when the app goes to the background, on an
// audio interruption and when the haptic server resets; the next pulse brings it back here.
static bool AtlasHapticEnsureRunning(void)
{
    if (sEngine == nil) return false;
    if (sEngineRunning.load()) return true;
    NSError* error = nil;
    if ([sEngine startAndReturnError:&error] && error == nil)
    {
        sEngineRunning.store(true);
        return true;
    }
    return false;
}

static void AtlasHapticFallback(float intensity)
{
    if (sMedium == nil)
    {
        sMedium = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
        sHeavy = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
    }
    UIImpactFeedbackGenerator* generator = intensity < 0.75f ? sMedium : sHeavy;
    [generator impactOccurredWithIntensity:intensity];
    [generator prepare];                                  // keeps the Taptic Engine ready for the next pulse
}

extern "C" void AtlasHapticPrepare(void)
{
    @autoreleasepool
    {
        if (sEngine == nil && CHHapticEngine.capabilitiesForHardware.supportsHaptics)
        {
            NSError* error = nil;
            CHHapticEngine* engine = [[CHHapticEngine alloc] initAndReturnError:&error];
            if (engine != nil && error == nil)
            {
                engine.playsHapticsOnly = YES;            // no audio session of its own beside Unity's
                engine.autoShutdownEnabled = NO;          // a cue must start at once, not after a start-up
                engine.stoppedHandler = ^(CHHapticEngineStoppedReason reason) { sEngineRunning.store(false); };
                engine.resetHandler = ^{ sEngineRunning.store(false); };
                sEngine = engine;
            }
        }
        AtlasHapticEnsureRunning();

        if (sMedium == nil)
        {
            sMedium = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
            sHeavy = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
        }
        [sMedium prepare];
        [sHeavy prepare];
    }
}

extern "C" void AtlasHapticPulse(float intensity)
{
    if (intensity < 0.0f) intensity = 0.0f;
    if (intensity > 1.0f) intensity = 1.0f;

    @autoreleasepool
    {
        if (AtlasHapticEnsureRunning())
        {
            NSError* error = nil;
            CHHapticEventParameter* strength = ATLAS_AUTORELEASE([[CHHapticEventParameter alloc]
                initWithParameterID:CHHapticEventParameterIDHapticIntensity value:intensity]);
            CHHapticEventParameter* sharpness = ATLAS_AUTORELEASE([[CHHapticEventParameter alloc]
                initWithParameterID:CHHapticEventParameterIDHapticSharpness value:(intensity < 0.75f ? 0.4f : 0.8f)]);
            CHHapticEvent* event = ATLAS_AUTORELEASE([[CHHapticEvent alloc]
                initWithEventType:CHHapticEventTypeHapticTransient parameters:@[strength, sharpness] relativeTime:0]);
            CHHapticPattern* pattern = ATLAS_AUTORELEASE([[CHHapticPattern alloc]
                initWithEvents:@[event] parameters:@[] error:&error]);
            if (pattern != nil && error == nil)
            {
                id<CHHapticPatternPlayer> player = [sEngine createPlayerWithPattern:pattern error:&error];
                if (player != nil && error == nil && [player startAtTime:CHHapticTimeImmediate error:&error] && error == nil)
                    return;
            }
            // The engine refused: it has probably just stopped. Start it again on the next pulse.
            sEngineRunning.store(false);
        }
        AtlasHapticFallback(intensity);
    }
}

extern "C" void AtlasHapticStop(void)
{
    if (sEngine == nil) return;
    sEngineRunning.store(false);
    [sEngine stopWithCompletionHandler:nil];
}
