using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class LeverAngles
{
    public float upDown;
    public float leftRight;

    public LeverAngles(float upDown, float leftRight)
    {
        this.upDown = upDown;
        this.leftRight = leftRight;
    }

    public bool isOperated()
    {
        if (this.upDown != 0 || this.leftRight != 0) return true;
        else return false;
    }

    public void clear()
    {
        this.upDown = 0F;
        this.leftRight = 0F;
    }
}


public class InputEvents
{
    private float _swing;
    private float _boom;
    private float _arm;
    private float _bucket;
    private float _trackRight;
    private float _trackLeft;

    private bool _updated;

    public void clear()
    {
        _swing = 0F;
        _boom = 0F;
        _arm = 0F;
        _bucket = 0F;
        _trackRight = 0F;
        _trackLeft = 0F;

        _updated = false;
    }

    public bool isUpdated
    {
        get { return _updated; }
    }

    public float swing
    {
        set { _swing = value; _updated = true; }
        get { return _swing; }
    }

    public float boom
    {
        set { _boom = value; _updated = true; }
        get => _boom;
    }

    public float arm
    {
        set { _arm = value; _updated = true; }
        get => _arm;
    }

    public float bucket
    {
        set { _bucket = value; _updated = true; }
        get => _bucket;
    }

    public float trackRight
    {
        set { _trackRight = value; _updated = true; }
        get => _trackRight;
    }

    public float trackLeft
    {
        set { _trackLeft = value; _updated = true; }
        get => _trackLeft;
    }

}


public class ExcavatorController : MonoBehaviour
{
    public float maxSpeed = 3F;
    public float creepSpeed = 1F;
    public float initialAccel = 15F;
    public float deltaAccel = 0.5F;
    public float maxAccel = 45F;
    public float deltaDirection = 2F;

    public bool enableRearCameras = false;

    public bool useHook = false;

    // --- Study additions ---
    // When false, the keyboard keys 1-6 (autonomous demo moves) are ignored.
    // The StudyManager turns this off so participants cannot trigger them by accident.
    public static bool AutonomousDemoEnabled = true;

    // Where the controls come from. A flight stick (Thrustmaster TCA or WingMan) drives everything,
    // and the F310 + keyboard keep working alongside it (see FlightStickInput.cs).
    [Header("Flight stick")]
    [Tooltip("Read a flight stick in addition to the F310 gamepad and keyboard.")]
    public bool useFlightStick = true;
    [Tooltip("Auto picks a Thrustmaster TCA if connected, otherwise a WingMan, otherwise any joystick.")]
    public FlightStickChoice flightStick = FlightStickChoice.Auto;
    [Tooltip("Auto: two sticks connected = gamepad-style two-stick layout, one stick = single-stick layout.")]
    public StickLayout stickLayout = StickLayout.Auto;
    [Tooltip("Show live stick values at the bottom of the screen (always shown while calibrating).")]
    public bool showFlightStickDebug = true;
    private IExcavatorInput input = new GamepadKeyboardInput();
    public IExcavatorInput InputSource { get => input; set => input = value; }

    // Read-only access for the study scripts (measurement bar, context monitor, logger).
    public Excavator Machine => excavator;
    public bool IsTravelMode => travel;
    public InputSnapshot LastInput { get; private set; }

    private DriveParams driveParams;

    private InputEvents inputEvents = new InputEvents();

    private Excavator excavator;

    bool travel = false;
    Image travelIndicator;
    Image operationIndicator;

    void Update()
    {
        // Study change: ignore the mode button while the simulation is frozen (setup, briefing, questionnaire).
        if (Time.timeScale == 0F || excavator == null) return;

        bool modeChange = input.ModeTogglePressedThisFrame();

        if (modeChange)
        {
            travel = !travel;
            ModeIndicatorIcons.Apply(travelIndicator, operationIndicator, travel);   // green square + icon = active mode
        }
    }

    void Awake()
    {
        if (useFlightStick && input is GamepadKeyboardInput)
        {
            var combined = new GamepadAndStickInput();
            combined.stick.choice = flightStick;
            combined.layout = stickLayout;
            input = combined;
        }
    }

    GUIStyle stickStyle;

    void OnGUI()
    {
        if (!(input is GamepadAndStickInput gi)) return;
        if (!showFlightStickDebug && !gi.IsCalibrating) return;
        GUI.depth = -1000; // bottom-centre, on top of the study panels
        if (stickStyle == null)
        {
            stickStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true };
            var bg = new Texture2D(1, 1); bg.SetPixel(0, 0, new Color(0F, 0F, 0F, 0.85F)); bg.Apply();
            stickStyle.normal.background = bg;
            stickStyle.padding = new RectOffset(12, 12, 8, 8);
        }
        stickStyle.normal.textColor = gi.IsCalibrating ? Color.yellow : Color.white;
        float w = Mathf.Min(1000F, Screen.width - 20F), h = 170F;
        GUI.Label(new Rect((Screen.width - w) / 2F, Screen.height - h - 10F, w, h), gi.DebugText(), stickStyle);
    }

    // Start is called before the first frame update
    void Start()
    {
        excavator = new Excavator(transform.root.gameObject);

        travelIndicator = GameObject.FindWithTag("TravelIndicator").GetComponent<Image>();
        operationIndicator = GameObject.FindWithTag("OperationIndicator").GetComponent<Image>();
        ModeIndicatorIcons.Setup(travelIndicator, operationIndicator);   // TRAVEL (tracks) / DIG (bucket) icons
        ModeIndicatorIcons.Apply(travelIndicator, operationIndicator, travel);
        float mass = gameObject.GetComponent<Rigidbody>().mass;
        driveParams = new DriveParams(mass, maxSpeed, creepSpeed, initialAccel, deltaAccel, maxAccel, deltaDirection);

        excavator.EnableRearCameras(enableRearCameras);

        excavator.useHook = useHook;
    }

    private LeverAngles rightOperationLeverAngles = new LeverAngles(0F, 0F);
    private LeverAngles leftOperationLeverAngles = new LeverAngles(0F, 0F);
    private LeverAngles rightTravelLeverAngles = new LeverAngles(0F, 0F);
    private LeverAngles leftTravelLeverAngles = new LeverAngles(0F, 0F);

    void FixedUpdate()
    {
        // Study change: if Start() failed, the excavator was never created. Stop here instead of
        // throwing an error every physics step; the FIRST red error in the Console shows the real cause.
        if (excavator == null) return;

        excavator.OrientHook();  // TODO: add hook operations

        //--- Code for autonomous operations from this line ---

        if (AutonomousDemoEnabled && !excavator.isInAutonomousDriving)
        {
            if (Keyboard.current != null && Keyboard.current.digit1Key.isPressed)
            {
                excavator.EnableCuttingEdges(false);
                StartCoroutine(excavator.Reset());
                GameObject target = GameObject.FindWithTag("Target1");
                StartCoroutine(excavator.MoveToTarget(target, driveParams, 40F, 40F));
            }
            if (Keyboard.current != null && Keyboard.current.digit2Key.isPressed)
            {

                excavator.EnableCuttingEdges(false);
                StartCoroutine(excavator.Reset());
                GameObject target2 = GameObject.FindWithTag("Target2");
                StartCoroutine(excavator.MoveToTarget(target2, driveParams, 40F, 40F));
            }
            if (Keyboard.current != null && Keyboard.current.digit3Key.isPressed)
            {
                excavator.EnableCuttingEdges(true);
                StartCoroutine(excavator.Reset());
                GameObject target3 = GameObject.FindWithTag("Target3");
                StartCoroutine(excavator.MoveToTarget(target3, driveParams, 40F, 40F));
            }
            if (Keyboard.current != null && Keyboard.current.digit4Key.isPressed)
            {
                excavator.EnableCuttingEdges(false);
                StartCoroutine(excavator.Reset());
                GameObject target4 = GameObject.FindWithTag("Target4");
                StartCoroutine(excavator.MoveToTarget(target4, driveParams, 50F, 50F));
            }
            if (Keyboard.current != null && Keyboard.current.digit5Key.isPressed)
            {
                excavator.EnableCuttingEdges(true);
                StartCoroutine(excavator.Reset());
                GameObject target5 = GameObject.FindWithTag("Target5");
                StartCoroutine(excavator.MoveToTarget(target5, driveParams, 140F, 40F));
            }
            if (Keyboard.current != null && Keyboard.current.digit6Key.isPressed)
            {
                excavator.EnableCuttingEdges(true);
                GameObject target6 = GameObject.FindWithTag("Target6");
                StartCoroutine(excavator.MoveToTarget(target6, driveParams, 40F, 140F));
            }
        }

        //--- Code for manual operations from this line ---
        var delta = Time.deltaTime * 30F;

        if (leftOperationLeverAngles.isOperated() || rightOperationLeverAngles.isOperated())
        {
            excavator.leftOperationLeverRotate(-leftOperationLeverAngles.leftRight, -leftOperationLeverAngles.upDown);
            excavator.rightOperationLeverRotate(-rightOperationLeverAngles.leftRight, -rightOperationLeverAngles.upDown);

            leftOperationLeverAngles.clear();
            rightOperationLeverAngles.clear();
        }


        if (leftTravelLeverAngles.isOperated() || rightTravelLeverAngles.isOperated())
        {
            excavator.leftTravelLeverRotate(-leftTravelLeverAngles.upDown);
            excavator.rightTravelLeverRotate(-rightTravelLeverAngles.upDown);

            excavator.rightPedalRotate(rightTravelLeverAngles.upDown * 2F);
            excavator.leftPedalRotate(leftTravelLeverAngles.upDown * 2F);

            leftTravelLeverAngles.clear();
            rightTravelLeverAngles.clear();

        }

        input.Read(travel, inputEvents);
        LastInput = new InputSnapshot(inputEvents);

        // Control
        if (inputEvents.isUpdated)
        {

            /* Swing */
            if (inputEvents.swing != 0F)
            {
                excavator.swingRotate(delta * 1F * inputEvents.swing);
                leftOperationLeverAngles.leftRight = 5F * inputEvents.swing;
            }

            /* Arm */
            if (inputEvents.arm != 0F)
            {
                excavator.armRotate(-delta * 1F * inputEvents.arm);
                leftOperationLeverAngles.upDown = 5F * inputEvents.arm;
            }

            /* Boom */
            if (inputEvents.boom != 0F)
            {
                excavator.boomRotate(-delta * 0.6F * inputEvents.boom);
                rightOperationLeverAngles.upDown = 5F * inputEvents.boom;
            }

            /* Bucket */
            if (inputEvents.bucket != 0F)
            {
                excavator.bucketRotate(delta * 1.5F * inputEvents.bucket);
                rightOperationLeverAngles.leftRight = 5F * inputEvents.bucket;
            }

            /* Tracks */
            if (inputEvents.trackRight != 0F || inputEvents.trackLeft != 0F)
            {
                float rotationRight = -delta * 0.6F * inputEvents.trackRight;
                float rotationLeft = delta * 0.6F * inputEvents.trackLeft;
                float deltaRotation = rotationRight + rotationLeft;
                float input = inputEvents.trackRight + inputEvents.trackLeft;
                if (input > 1F) input = 1F;
                else if (input < -1F) input = -1F;
                float accel = 0F;
                if (input != 0F) accel = driveParams.initialAccel * Mathf.Sign(input) + (driveParams.maxAccel - driveParams.initialAccel) * input;
                excavator.Move(deltaRotation, accel, driveParams);
                rightTravelLeverAngles.upDown = 5F * inputEvents.trackRight;
                leftTravelLeverAngles.upDown = 5F * inputEvents.trackLeft;
            }

            inputEvents.clear();
        }

        if (rightOperationLeverAngles.isOperated() || leftOperationLeverAngles.isOperated())
        {
            excavator.rightOperationLeverRotate(rightOperationLeverAngles.leftRight, rightOperationLeverAngles.upDown);
            excavator.leftOperationLeverRotate(leftOperationLeverAngles.leftRight, leftOperationLeverAngles.upDown);
        }

        if (rightTravelLeverAngles.isOperated() || leftTravelLeverAngles.isOperated())
        {
            excavator.rightTravelLeverRotate(rightTravelLeverAngles.upDown);
            excavator.leftTravelLeverRotate(leftTravelLeverAngles.upDown);

            excavator.rightPedalRotate(-rightTravelLeverAngles.upDown * 2F);
            excavator.leftPedalRotate(-leftTravelLeverAngles.upDown * 2F);
        }

    }

}
