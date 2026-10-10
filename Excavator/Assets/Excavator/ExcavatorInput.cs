using UnityEngine.InputSystem;

// ---------------------------------------------------------------------------
// INPUT LAYER
// The excavator controller asks an "input source" for lever values instead of
// reading the gamepad directly. To support real excavator joysticks later,
// write a new class that implements IExcavatorInput and assign it to
// ExcavatorController.InputSource. Nothing else has to change.
// ---------------------------------------------------------------------------

public interface IExcavatorInput
{
    // Fill in lever values (-1..1) for this physics step.
    void Read(bool travelMode, InputEvents events);

    // True on the frame the operator switches between travel and operation mode.
    bool ModeTogglePressedThisFrame();
}

// A copy of the lever values from one physics step, kept for logging and
// for measuring when the operator reacted to a hazard.
public struct InputSnapshot
{
    public float swing, boom, arm, bucket, trackLeft, trackRight;

    public InputSnapshot(InputEvents e)
    {
        swing = e.swing; boom = e.boom; arm = e.arm; bucket = e.bucket;
        trackLeft = e.trackLeft; trackRight = e.trackRight;
    }

    public bool Any =>
        swing != 0 || boom != 0 || arm != 0 || bucket != 0 || trackLeft != 0 || trackRight != 0;
}

// Logitech F310 gamepad + keyboard (same mapping the project had before).
//   Operation mode: left stick X = swing, left stick Y = arm,
//                   right stick X = bucket, right stick Y = boom
//   Travel mode:    left stick Y = left track, right stick Y = right track
//   B button (east) toggles travel / operation mode.
public class GamepadKeyboardInput : IExcavatorInput
{
    // Stick movement smaller than this is ignored (worn sticks drift slightly).
    public float deadZone = 0.1F;

    float Dz(float v) => (v > -deadZone && v < deadZone) ? 0F : v;

    public bool ModeTogglePressedThisFrame()
    {
        return Gamepad.current != null && Gamepad.current.buttonEast.wasReleasedThisFrame;
    }

    public void Read(bool travelMode, InputEvents e)
    {
        ReadKeyboard(e);
        ReadGamepad(travelMode, e);
    }

    void ReadGamepad(bool travelMode, InputEvents e)
    {
        var pad = Gamepad.current;
        if (pad == null) return;

        float lx = Dz(pad.leftStick.x.ReadValue());
        float ly = Dz(pad.leftStick.y.ReadValue());
        float rx = Dz(pad.rightStick.x.ReadValue());
        float ry = Dz(pad.rightStick.y.ReadValue());

        if (!travelMode)
        {
            if (lx != 0) e.swing = lx;
            if (ly != 0) e.arm = ly;
            if (rx != 0) e.bucket = rx;
            if (ry != 0) e.boom = ry;
        }
        else
        {
            if (ly != 0) e.trackLeft = ly;
            if (ry != 0) e.trackRight = ry;
        }
    }

    void ReadKeyboard(InputEvents e)
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.hKey.isPressed) e.swing = 1F; else if (kb.fKey.isPressed) e.swing = -1F;
        if (kb.tKey.isPressed) e.arm = 1F; else if (kb.gKey.isPressed) e.arm = -1F;
        if (kb.iKey.isPressed) e.boom = 1F; else if (kb.kKey.isPressed) e.boom = -1F;
        if (kb.lKey.isPressed) e.bucket = 1F; else if (kb.jKey.isPressed) e.bucket = -1F;
        if (kb.uKey.isPressed) e.trackRight = 1F; else if (kb.oKey.isPressed) e.trackRight = -1F;
        if (kb.yKey.isPressed) e.trackLeft = 1F; else if (kb.rKey.isPressed) e.trackLeft = -1F;
    }
}
