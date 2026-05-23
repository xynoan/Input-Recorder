# Input Recorder

A small Windows desktop utility that records global mouse and keyboard input and plays it back.

## Requirements

- Windows
- .NET 9 SDK

## Run

```powershell
dotnet run
```

## Use

1. Click **Record**.
2. Move/click/scroll the mouse, or type on the keyboard.
3. Click **Stop Recording** or press **F8**.
4. In **Playback repeat**, enable **Enable loop playback** and set **Number of times to play** if you want repeated playback.
5. Click **Play**.
6. Click **Stop Playback** or press **F8** to stop playback.

Playback sends real mouse and keyboard input to Windows, so keep focus away from sensitive apps while testing.
