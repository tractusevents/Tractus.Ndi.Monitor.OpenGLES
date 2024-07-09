# Simple Source Monitor for NDI

Uses OpenGL ES and Silk.NET to tune into an NDI source. 

At startup, you can specify window `width`, `height`, and a `source` to tune into.

Example:

`Tractus.Ndi.Monitor.OpenGLES.exe -w=1920 -h=1080 -src="BIRDDOG-X1 (CAM)"`

## Eventual Goals

- Multiple instances cooperating
- Alpha overlays of a 2nd source
- Audio output
- UMD overlays
- YUV to RGB via pixel shaders