// Copyright Allen Institute for Artificial Intelligence 2017
//Check Assets/Prefabs/DebugController for ReadMe on how to use this Debug Controller
using UnityEngine;
using Random = UnityEngine.Random;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityStandardAssets.CrossPlatformInput;
using UnityStandardAssets.Utility;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.Mathematics;


namespace UnityStandardAssets.Characters.FirstPerson
{
	[RequireComponent(typeof (CharacterController))]
    public class DebugFPSAgentController : MonoBehaviour
	{
        //for use with mouse/keyboard input
		[SerializeField] protected bool m_IsWalking;
		[SerializeField] protected float m_WalkSpeed;
		[SerializeField] protected float m_RunSpeed;
        protected float m_CustomSpeedFactor;

        [SerializeField] protected float m_GravityMultiplier;
		[SerializeField] protected MouseLook m_MouseLook;

        [SerializeField] protected GameObject Debug_Canvas = null;
//        [SerializeField] private GameObject Inventory_Text = null;
		[SerializeField] protected GameObject InputMode_Text = null;
        [SerializeField] protected float MaxViewDistance = 5.0f;
        [SerializeField] private float MaxChargeThrowSeconds = ObjectHighlightController.DEFAULT_MAX_CHARGE_THROW_SECONDS;
        [SerializeField] private float MaxThrowForce = ObjectHighlightController.DEFAULT_MAX_THROW_FORCE;
        [SerializeField] private Space objectRotateRelativeTo = Space.Self;
        [SerializeField] private float objectRotateStep = -10.0f;
        // public bool FlightMode = false;

        public bool FPSEnabled = true;
		public GameObject InputFieldObj = null;

        private ObjectHighlightController highlightController = null;

        private Camera m_Camera;
        private Vector2 m_Input;
        private Vector3 m_MoveDir = Vector3.zero;
        private CharacterController m_CharacterController;
        private PhysicsRemoteFPSAgentController PhysicsController;
        private bool scroll2DEnabled = true;

        protected bool enableHighlightShader = true;

        private void Start()
        {
            m_CharacterController = GetComponent<CharacterController>();
            m_Camera = Camera.main;
            m_MouseLook.Init(transform, m_Camera.transform);

            //find debug canvas related objects
            Debug_Canvas = GameObject.Find("DebugCanvasPhysics");
						InputMode_Text = GameObject.Find("DebugCanvasPhysics/InputModeText");

            InputFieldObj = GameObject.Find("DebugCanvasPhysics/InputField");
            PhysicsController = gameObject.GetComponent<PhysicsRemoteFPSAgentController>();

            highlightController = new ObjectHighlightController(PhysicsController, MaxViewDistance, enableHighlightShader, true, MaxThrowForce, MaxChargeThrowSeconds);

            //if this component is enabled, turn on the targeting reticle and target text
            if (this.isActiveAndEnabled)
            {
				Debug_Canvas.GetComponent<Canvas>().enabled = true;
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }

            // FlightMode = PhysicsController.FlightMode;

            #if UNITY_WEBGL
                FPSEnabled = false;
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
                HideHUD();
            #endif
            #if !UNITY_EDITOR && UNITY_WEBGL
                WebGLInput.captureAllKeyboardInput = false;
            #endif

            m_CustomSpeedFactor = 2.0f;
        }
        public Vector3 ScreenPointMoveHand(float yOffset)
		{
			RaycastHit hit;
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
			//shoot a ray out based on mouse position
			Physics.Raycast(ray, out hit);
            return hit.point + new Vector3(0, yOffset, 0);
		}

        public void HideHUD()
        {
            if (InputMode_Text != null) {
                InputMode_Text.SetActive(false);
            }
						if (InputFieldObj != null) {
								InputFieldObj.SetActive(false);
						}
            var background = GameObject.Find("DebugCanvasPhysics/InputModeText_Background");
						if (background != null) {
								background.SetActive(false);
						}
        }

        public void SetScroll2DEnabled(bool enabled)
        {
            this.scroll2DEnabled = enabled;
        }

        public void OnEnable()
        {

                FPSEnabled = true;
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;

                InputMode_Text = GameObject.Find("DebugCanvasPhysics/InputModeText");
                InputFieldObj = GameObject.Find("DebugCanvasPhysics/InputField");
                if (InputMode_Text) {
                    InputMode_Text.GetComponent<Text>().text = "FPS Mode";
                }


                Debug_Canvas = GameObject.Find("DebugCanvasPhysics");

                Debug_Canvas.GetComponent<Canvas>().enabled = true;
        }

        public void OnDisable()
        {
            DisableMouseControl();
            //  if (InputFieldObj) {
            //     InputFieldObj.SetActive(true);
            //  }
        }

        public void EnableMouseControl()
        {
            FPSEnabled = true;
            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }

        public void DisableMouseControl()
        {
            Debug.Log("Disabled mouse");
            FPSEnabled = false;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void DebugKeyboardControls()
		{
			//swap between text input and not
			if (Input.GetKeyDown(KeyCode.BackQuote) || Input.GetKeyDown(KeyCode.Escape))
            {
				//Switch to Text Mode
                if (FPSEnabled)
                {
                    if (InputMode_Text) {
                        InputMode_Text.GetComponent<Text>().text = "FPS Mode";
                    }
                    FPSEnabled = false;
                    Cursor.visible = true;
                    Cursor.lockState = CursorLockMode.None;
                    return;
                }
                else
                 {
                    if (InputMode_Text) {
					    InputMode_Text.GetComponent<Text>().text = "FPS Mode (mouse free)";
                    }
                    FPSEnabled = true;
                    Cursor.visible = false;
                    Cursor.lockState = CursorLockMode.Locked;
                    return;
                }

            }

            // 1D Scroll for hand movement
            if (!scroll2DEnabled && this.PhysicsController.WhatAmIHolding() != null)
            {
                var scrollAmount = Input.GetAxis("Mouse ScrollWheel");

                var eps = 1e-6;
                if (Mathf.Abs(scrollAmount) > eps) {
                    ServerAction action = new ServerAction
                    {
                        action = "MoveHandAhead",
                        moveMagnitude = scrollAmount
                    };
                    this.PhysicsController.ProcessControlCommand(action);
                }

            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                var action = new ServerAction
                {
                    action = "InitialRandomSpawn",
                    randomSeed = 0,
                    forceVisible = false,
                    numPlacementAttempts = 5,
                    placeStationary = true
                };
                PhysicsController.ProcessControlCommand(action);
            }

            //if(Input.GetKey(KeyCode.Space))
            //{
            //    Cursor.visible = true;
            //    Cursor.lockState = CursorLockMode.None;
            //}

            //if(Input.GetKeyUp(KeyCode.Space))
            //{
            //    Cursor.visible = false;
            //    Cursor.lockState = CursorLockMode.Locked;
            //}

            if (Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl) || Input.GetKeyDown(KeyCode.C))
            {
                String act;
                if (PhysicsController.isStanding())
                {
                    act = "Crouch";
                }
                else
                {
                    act = "Stand";
                }
                var action = new ServerAction
                {
                    action = act,
                };
                this.PhysicsController.ProcessControlCommand(action);
            }
        }

        private void RotateHeldObjectControls()
        {
            if (Input.GetKeyDown(KeyCode.Z))
            {
                if (this.objectRotateRelativeTo == Space.Self)
                {
                    this.objectRotateRelativeTo = Space.World;
                } else
                {
                    this.objectRotateRelativeTo = Space.Self;
                }
                Console.WriteLine(String.Format("Switching rotation orientation to {0}", this.objectRotateRelativeTo));
            }


            var action = new ServerAction
            {
                action = "RotateObject",
                relativeTo = this.objectRotateRelativeTo
            };
            bool rotate = false;

            if (Input.GetKey(KeyCode.T))
            {
                rotate = true;
                action.x = -this.objectRotateStep;
            }
            else if (Input.GetKey(KeyCode.Y))
            {
                rotate = true;
                action.x = this.objectRotateStep;
            }

            if (Input.GetKey(KeyCode.G))
            {
                rotate = true;
                action.y = -this.objectRotateStep;
            }
            else if (Input.GetKey(KeyCode.H))
            {
                rotate = true;
                action.y = this.objectRotateStep;
            }

            if (Input.GetKey(KeyCode.B))
            {
                rotate = true;
                action.z = -this.objectRotateStep;
            }
            else if (Input.GetKey(KeyCode.N))
            {
                rotate = true;
                action.z = this.objectRotateStep;
            }

            if (rotate)
            {
                Console.WriteLine(String.Format("Sending rotate control command: {0},{1},{2} | {3}",
                    action.x, action.y, action.z, action.relativeTo));
                this.PhysicsController.ProcessControlCommand(action);
            }
        }

		private void Update()
        {
            highlightController.UpdateHighlightedObject(new Vector3(Screen.width / 2, Screen.height / 2));
            highlightController.MouseControls();

			DebugKeyboardControls();
            RotateHeldObjectControls();
            ///////////////////////////////////////////////////////////////////////////
            //we are not in focus mode, so use WASD and mouse to move around
            if (FPSEnabled)
			{
				FPSInput();
				if(Cursor.visible == false)
				{
                    //accept input to update view based on mouse input
                    MouseRotateView();
                }
            }
        }

        public void OnGUI()
        {
            if (Event.current.type == EventType.ScrollWheel && scroll2DEnabled)
            {
                GameObject heldObject = this.PhysicsController.WhatAmIHolding();
                if (heldObject != null)
                {
                    var scrollAmount = Event.current.delta;
                    var eps = 1e-6;
                    if (Mathf.Abs(scrollAmount.x) > eps || Mathf.Abs(scrollAmount.y) > eps)
                    {
                        ServerAction action = new ServerAction
                        {
                            action = "MoveHandDelta",
                            objectName = heldObject.name,
                            // Removing support for scrolling an object sideways, as it's non-intuitive
                            x = 0, //x = scrollAmount.x * 0.05f, 
                            z = scrollAmount.y * -0.05f,
                            y = 0,
                        };
                        this.PhysicsController.ProcessControlCommand(action);
                    }

                }
            }
        }

	    private void GetInput(out float speed)
		{
			// Read input
			float horizontal = CrossPlatformInputManager.GetAxis("Horizontal");
			float vertical = CrossPlatformInputManager.GetAxis("Vertical");

			//bool waswalking = m_IsWalking;

			#if !MOBILE_INPUT
			// On standalone builds, walk/run speed is modified by a key press.
			// keep track of whether or not the character is walking or running
			m_IsWalking = !Input.GetKey(KeyCode.LeftShift);
			#endif
			// set the desired speed to be walking or running
			speed = (m_IsWalking ? m_WalkSpeed : m_RunSpeed) * m_CustomSpeedFactor;
			m_Input = new Vector2(horizontal, vertical);

			// normalize input if it exceeds 1 in combined length:
			if (m_Input.sqrMagnitude > 1)
			{
				m_Input.Normalize();
			}

		}

        public MouseLook GetMouseLook() {
            return m_MouseLook;
        }

		private void MouseRotateView()
		{
            // ??? why are these backward? at least they were in the code in MouseLook.cs
            float xRot = CrossPlatformInputManager.GetAxis("Mouse X") * m_MouseLook.XSensitivity;
            float yRot = CrossPlatformInputManager.GetAxis("Mouse Y") * m_MouseLook.YSensitivity;

            if (xRot != 0 || yRot != 0)
            {
                bool canRotateX = true;
                bool canRotateY = true;
                string directionX = "";
                string directionY = "";
                if (xRot != 0)
                {
                    directionX = xRot > 0 ? "right" : "left";
                    canRotateX = PhysicsController.CheckIfAgentCanRotate(directionX, xRot);
                }
                if (yRot != 0)
                {
                    directionY = yRot > 0 ? "up" : "down";
                    canRotateY = canRotateY && PhysicsController.CheckIfAgentCanRotate(directionY, yRot);
                }
                
                //Console.WriteLine(String.Format("Mouse rotation | x: {0} | directionX: {1} | canRotateX: {2} | y: {3} | directionY: {4} | canRotateY: {5}",
                //    xRot, directionX, canRotateX, yRot, directionY, canRotateY));
                if (canRotateX && canRotateY)
                {
                    m_MouseLook.LookRotation(transform, m_Camera.transform);
                }

            }
		}

        private void FPSInput()
		{
            // This is the actual movement function
            //Console.WriteLine("DebugFPSAgentController.FPSInput reached");
            //take WASD input and do magic, turning it into movement!
            float speed;
            GetInput(out speed);
            // always move along the camera forward as it is the direction that it being aimed at
            Vector3 desiredMove = transform.forward * m_Input.y + transform.right * m_Input.x;
            // get a normal for the surface that is being touched to move along it
            RaycastHit hitInfo;

            Physics.SphereCast(transform.position, m_CharacterController.radius, Vector3.down, out hitInfo,
                m_CharacterController.height / 2f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            desiredMove = Vector3.ProjectOnPlane(desiredMove, hitInfo.normal).normalized;

            //m_MoveDir.x = desiredMove.x * speed;
            //m_MoveDir.z = desiredMove.z * speed;
            // before it wasn't writing y, so it was adding the effect of gravity (see below), which would continue accumulating in the negative y direction
            m_MoveDir = desiredMove * speed;  

            // if(!FlightMode)
            m_MoveDir += Physics.gravity * m_GravityMultiplier * Time.fixedDeltaTime;

            // Copied over from BaseFPSAgenController.moveInDirection for controller movement purposes
            float angle = Vector3.Angle(transform.forward, Vector3.Normalize(m_MoveDir));
            float right = Vector3.Dot(transform.right, m_MoveDir);
            if (right < 0)
            {
                angle = 360f - angle;
            }
            int angleInt = Mathf.RoundToInt(angle) % 360;

            //if (m_Input != Vector2.zero)
            //{
            //Console.WriteLine(String.Format("Speed: {0}, m_Input: {1}, desiredMove: {2}, m_MoveDir: {3}",
            //    speed, m_Input, desiredMove, m_MoveDir));
            //Console.WriteLine(String.Format("isWalking: {0}, m_WalkSpeed: {1}, m_RunSpeed: {2}, m_CustomSpeedFactor: {3}",
            //m_IsWalking, m_WalkSpeed, m_RunSpeed, m_CustomSpeedFactor));
            //}

            //added this check so that move is not called if/when the Character Controller's capsule is disabled. Right now the capsule is being disabled when open/close animations are in progress so yeah there's that
            if(m_CharacterController.enabled == true) {
                // Trying to mimic some of the move validation logic
                Vector3 moveDelta = m_MoveDir * Time.fixedDeltaTime;
                Vector3 targetPosition = transform.position + moveDelta;

                if (PhysicsController.checkIfSceneBoundsContainTargetPosition(targetPosition) &&
                    PhysicsController.CheckIfItemBlocksAgentMovement(moveDelta.magnitude, angleInt, false) &&
                    PhysicsController.CheckIfAgentCanMove(moveDelta.magnitude, angleInt))
                {
                    CollisionFlags movementResult = m_CharacterController.Move(moveDelta);


                    #if UNITY_WEBGL
                    JavaScriptInterface jsInterface = this.GetComponent<JavaScriptInterface>();
                    if ((jsInterface != null) && (math.any(m_Input)))
                    {
                        MovementWrapper movement = new MovementWrapper
                        {
                            position = transform.position,
                            rotationEulerAngles = transform.rotation.eulerAngles,
                            direction = m_MoveDir,
                            targetPosition = desiredMove,
                            input = m_Input,
                            //movement.succeeded = movementResult == CollisionFlags.None;
                            touchingSide = (movementResult & CollisionFlags.Sides) != 0,
                            touchingCeiling = (movementResult & CollisionFlags.Above) != 0,
                            touchingFloor = (movementResult & CollisionFlags.Below) != 0
                        };

                        jsInterface.SendMovementData(movement);
                    }
                    #endif
                }
                else if (math.any(m_Input))
                {
                    Console.WriteLine(String.Format("Move failed: {0} && {1} && {2}",
                        PhysicsController.checkIfSceneBoundsContainTargetPosition(targetPosition),
                        PhysicsController.CheckIfItemBlocksAgentMovement(moveDelta.magnitude, angleInt, false),
                        PhysicsController.CheckIfAgentCanMove(moveDelta.magnitude, angleInt)));
                    Console.WriteLine(String.Format("Current position: {0} | desired move: {1} | move direction: {2} | move delta: {3} Target position: {4} | move magnitude: {5} | angleInt: {6}",
                        transform.position, desiredMove, m_MoveDir, moveDelta, targetPosition, moveDelta.magnitude, angleInt));
                    if (!PhysicsController.checkIfSceneBoundsContainTargetPosition(targetPosition))
                    {
                        Console.WriteLine(String.Format("Scene bounds: {0}", PhysicsController.sceneBounds));
                    }
                }
            }
		}


	}
}
