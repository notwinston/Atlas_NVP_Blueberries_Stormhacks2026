#import <Foundation/Foundation.h>

// NSProcessInfoThermalState: 0 nominal, 1 fair, 2 serious, 3 critical.
extern "C" int AtlasThermalState(void)
{
    return (int)[NSProcessInfo processInfo].thermalState;
}
