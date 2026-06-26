// Re-export SlideShowController types from FreeP.App.Presentation so test code that previously
// used `using FreeP.App.Host;` continues to compile without changes.

global using AnimationStep       = FreeP.App.Compositor.AnimationStep;
global using AdvanceResult       = FreeP.App.Compositor.AdvanceResult;
global using BackResult          = FreeP.App.Compositor.BackResult;
global using SlideShowController = FreeP.App.Compositor.SlideShowController;
