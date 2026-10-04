using UnityEngine.Scripting;

// Nothing references this assembly (contract README F1): it reaches the app through the method of
// VisionHandsSource marked RuntimeInitializeOnLoadMethod. The linker keeps such a method, but it has to look
// at the assembly to find it; this makes sure it does at every stripping level.
[assembly: AlwaysLinkAssembly]
