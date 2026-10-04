#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

// Haptic pulses for the Bump Guard warning cue (Master Plan section 4.10, task P1-C14).
// One UIKit impact per call: the medium generator below strength 0.75 (WARN), the heavy one from there up
// (STRONG and tracking lost). The C# side (Atlas.Safety.IosHaptics) times the pulses: 2 per second for WARN,
// 6 per second for STRONG.
// Every function is called on the main thread, as UIKit requires.
// UIKit only, on purpose: Unity already links it, so the generated Xcode project needs no extra framework.

static UIImpactFeedbackGenerator* sMedium = nil;          // kept for the life of the app
static UIImpactFeedbackGenerator* sHeavy = nil;

static void AtlasHapticEnsureGenerators(void)
{
    if (sMedium == nil)
        sMedium = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
    if (sHeavy == nil)
        sHeavy = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];
}

// Creates the generators and wakes the Taptic Engine, so that the first pulse of a cue is not late.
extern "C" void AtlasHapticPrepare(void)
{
    AtlasHapticEnsureGenerators();
    [sMedium prepare];
    [sHeavy prepare];
}

extern "C" void AtlasHapticPulse(float intensity)
{
    if (!(intensity >= 0.0f)) intensity = 0.0f;           // also catches NaN
    if (intensity > 1.0f) intensity = 1.0f;

    AtlasHapticEnsureGenerators();
    UIImpactFeedbackGenerator* generator = intensity < 0.75f ? sMedium : sHeavy;
    [generator impactOccurredWithIntensity:(CGFloat)intensity];
    [generator prepare];                                  // keeps the Taptic Engine ready for the next pulse
}

// Nothing to stop: a generator holds no engine, and the system lets the Taptic Engine idle by itself a few
// seconds after the last prepare. Kept so that the C# side has one shape on every haptic back end.
extern "C" void AtlasHapticStop(void)
{
}
