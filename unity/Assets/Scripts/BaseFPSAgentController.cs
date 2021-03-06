// Copyright Allen Institute for Artificial Intelligence 2017
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityStandardAssets.CrossPlatformInput;
using UnityStandardAssets.Utility;
using UnityEngine;
using Random = UnityEngine.Random;
using UnityStandardAssets.ImageEffects;
using System.Linq;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.AI;

namespace UnityStandardAssets.Characters.FirstPerson
{
	[RequireComponent(typeof(CharacterController))]

	abstract public class BaseFPSAgentController : MonoBehaviour
	{
		//debug draw bounds of objects in editor
        #if UNITY_EDITOR
        protected List<Bounds> gizmobounds = new List<Bounds>();
        #endif
        [SerializeField] public SimObjPhysics[] VisibleSimObjPhysics
        {
            get;
            protected set;
        }
        [SerializeField] protected bool IsHandDefault = true;
        [SerializeField] protected GameObject ItemInHand = null; //current object in inventory
        [SerializeField] protected GameObject AgentHand = null;
        [SerializeField] protected GameObject DefaultHandPosition = null;
        [SerializeField] protected Transform rotPoint;
        [SerializeField] protected GameObject DebugPointPrefab;
        [SerializeField] private GameObject GridRenderer = null;
        [SerializeField] protected GameObject DebugTargetPointPrefab;
        [SerializeField] protected bool inTopLevelView = false;
        [SerializeField] protected Vector3 lastLocalCameraPosition;
        [SerializeField] protected Quaternion lastLocalCameraRotation;
        public float autoResetTimeScale = 1.0f;

        public Vector3[] reachablePositions = new Vector3[0];
        protected float gridVisualizeY = 0.005f; //used to visualize reachable position grid, offset from floor
        public Bounds sceneBounds = new Bounds(
            new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity),
            new Vector3(-float.PositiveInfinity, -float.PositiveInfinity, -float.PositiveInfinity)
        );
        protected HashSet<int> initiallyDisabledRenderers = new HashSet<int>();
		// first person controller parameters
		[SerializeField]
		protected bool m_IsWalking;
		[SerializeField]
		protected float m_WalkSpeed;
		[SerializeField]
		protected float m_RunSpeed;
		[SerializeField]
		protected float m_GravityMultiplier;
		protected static float gridSize = 0.25f;
        //determins default move distance for move actions
		protected float moveMagnitude;
        //determines rotation increment of rotate functions
        protected float rotateStepDegrees = 90.0f;
        protected bool snapToGrid;
		protected bool continuousMode;//deprecated, use snapToGrid instead
		public ImageSynthesis imageSynthesis;
        public GameObject VisibilityCapsule = null;//used to keep track of currently active VisCap: see different vis caps for modes below
        public GameObject TallVisCap;//meshes used for Tall mode
        public GameObject BotVisCap;//meshes used for Bot mode
        public GameObject DroneVisCap;//meshes used for Drone mode
        public GameObject DroneBasket;//reference to the drone's basket object
        private bool isVisible = true;
        public bool inHighFrictionArea = false;

        public bool IsVisible
        {
			get
            { return isVisible; }

			set
            {
                //first default all Vis capsules of all modes to not enabled
                HideAllAgentRenderers();

                //The VisibilityCapsule will be set to either Tall or Bot
                //from the SetAgentMode call in BaseFPSAgentController's Initialize()
                foreach (Renderer r in VisibilityCapsule.GetComponentsInChildren<Renderer>())
                {
                    r.enabled = value;
                }

				isVisible = value;
			}
        }

		protected float maxDownwardLookAngle = 60f;
		protected float maxUpwardLookAngle = 30f;
		//allow agent to push sim objects that can move, for physics
		protected bool PushMode = false;
		protected int actionCounter;
		protected Vector3 targetTeleport;
        public AgentManager agentManager;
		public string[] excludeObjectIds = new string[0];
		public Camera m_Camera;
        [SerializeField] protected float cameraOrthSize;
		protected float m_XRotation;
		protected float m_ZRotation;
		protected Vector2 m_Input;
		protected Vector3 m_MoveDir = Vector3.zero;
		public CharacterController m_CharacterController;
		protected CollisionFlags m_CollisionFlags;
		protected Vector3 lastPosition;
		protected string lastAction;
		protected bool lastActionSuccess;
		protected string lastActionObjectType;
		protected string lastActionObjectId;
        protected string lastActionObjectName;
		protected string lastActionReceptacleObjectType;
		protected string lastActionRceptacleObjectId;
        protected float lastActionX;
        protected float lastActionY;
        protected float lastActionZ;
        protected string errorMessage;
		protected ServerActionErrorCode errorCode;
		public bool actionComplete;
		public System.Object actionReturn;
        [SerializeField] protected Vector3 standingLocalCameraPosition;
        [SerializeField] protected Vector3 crouchingLocalCameraPosition;
        public float maxVisibleDistance = 1.5f; //changed from 1.0f to account for objects randomly spawned far away on tables/countertops, which would be not visible at 1.0f
        protected float[, , ] flatSurfacesOnGrid = new float[0, 0, 0];
        protected float[, ] distances = new float[0, 0];
        protected float[, , ] normals = new float[0, 0, 0];
		protected bool[, ] isOpenableGrid = new bool[0, 0];
        protected string[] segmentedObjectIds = new string[0];
        [SerializeField] public string[] objectIdsInBox = new string[0];
        protected int actionIntReturn;
        protected float actionFloatReturn;
        protected float[] actionFloatsReturn;
        protected Vector3[] actionVector3sReturn;
        protected string[] actionStringsReturn;
        public bool alwaysReturnVisibleRange = false;
		// initial states
		protected Vector3 init_position;
		protected Quaternion init_rotation;
		public int actionDuration = 3;

		// internal state variables
		private float lastEmitTime;
		protected List<string> collisionsInAction;// tracking collided objects
		protected string[] collidedObjects;// container for collided objects
		protected Quaternion targetRotation;
        // Javascript communication
        private JavaScriptInterface jsInterface = null;
        private ServerAction currentServerAction;
		public Quaternion TargetRotation
		{
			get { return targetRotation; }
		}

        private PhysicsSceneManager _physicsSceneManager = null;
        //use as reference to the PhysicsSceneManager object
        protected PhysicsSceneManager physicsSceneManager
        {
            get {
                if (_physicsSceneManager == null) {
                    _physicsSceneManager = GameObject.Find("PhysicsSceneManager").GetComponent<PhysicsSceneManager>();
                }
                return _physicsSceneManager;
            }
        }

        //reference to prefab for activiting the cracked camera effect via CameraCrack()
        [SerializeField] GameObject CrackedCameraCanvas = null;

		// Initialize parameters from environment variables
		protected virtual void Awake()
		{
            #if UNITY_WEBGL
                this.jsInterface = this.GetComponent<JavaScriptInterface>();
                this.jsInterface.enabled = true;
            #endif

            // character controller parameters
            m_CharacterController = GetComponent<CharacterController>();
			this.m_WalkSpeed = 10;
			this.m_RunSpeed = 20;
			this.m_GravityMultiplier = 2;

		}

		// Use this for initialization
		public virtual void Start()
		{
			m_Camera = this.gameObject.GetComponentInChildren<Camera>();

			// set agent initial states
			targetRotation = transform.rotation;
			collidedObjects = new string[0];
			collisionsInAction = new List<string>();

            //setting default renderer settings
            //this hides renderers not used in tall mode, and also sets renderer
            //culling in FirstPersonCharacterCull.cs to ignore tall mode renderers
            HideAllAgentRenderers();

			// record initial positions and rotations
			init_position = transform.position;
			init_rotation = transform.rotation;

			agentManager = GameObject.Find("PhysicsSceneManager").GetComponentInChildren<AgentManager>();

            //default nav mesh agent to false cause WHY DOES THIS BREAK THINGS I GUESS IT DOESN TLIKE TELEPORTING
            this.GetComponent<NavMeshAgent>().enabled = false;

            // Recordining initially disabled renderers and scene bounds
            //then setting up sceneBounds based on encapsulating all renderers
            foreach (Renderer r in GameObject.FindObjectsOfType<Renderer>()) {
                if (!r.enabled) {
                    initiallyDisabledRenderers.Add(r.GetInstanceID());
                } else {
                    sceneBounds.Encapsulate(r.bounds);
                }
            }

            //On start, activate gravity
            Vector3 movement = Vector3.zero;
            movement.y = Physics.gravity.y * m_GravityMultiplier;
            m_CharacterController.Move(movement);
		}

        //defaults all agent renderers, from all modes (tall, bot, drone), to hidden for initialization default
        protected void HideAllAgentRenderers()
        {
            foreach(Renderer r in TallVisCap.GetComponentsInChildren<Renderer>())
            {
                if(r.enabled)
                {
                    r.enabled = false;
                }
            }

            foreach(Renderer r in BotVisCap.GetComponentsInChildren<Renderer>())
            {
                if(r.enabled)
                {
                    r.enabled = false;
                }
            }

            foreach(Renderer r in DroneVisCap.GetComponentsInChildren<Renderer>())
            {
                if(r.enabled)
                {
                    r.enabled = false;
                }
            }
        }

		public void actionFinished(bool success, System.Object actionReturn=null)
		{

			if (actionComplete)
			{
				Debug.LogError ("ActionFinished called with actionComplete already set to true");
			}

            if (this.jsInterface)
            {
                // TODO: Check if the reflection method call was successfull add that to the sent event data
                this.jsInterface.SendAction(currentServerAction);
            }

            lastActionSuccess = success;
			this.actionComplete = true;
			this.actionReturn = actionReturn;
			actionCounter = 0;
			targetTeleport = Vector3.zero;
		}

        public Vector3[] getReachablePositions(float gridMultiplier = 1.0f, int maxStepCount = 10000, bool visualize = false, Color? gridColor = null) { //max step count represents a 100m * 100m room. Adjust this value later if we end up making bigger rooms?
            CapsuleCollider cc = GetComponent<CapsuleCollider>();

            float sw = m_CharacterController.skinWidth;
            Queue<Vector3> pointsQueue = new Queue<Vector3>();
            pointsQueue.Enqueue(transform.position);

            //float dirSkinWidthMultiplier = 1.0f + sw;
            Vector3[] directions = {
                new Vector3(1.0f, 0.0f, 0.0f),
                new Vector3(0.0f, 0.0f, 1.0f),
                new Vector3(-1.0f, 0.0f, 0.0f),
                new Vector3(0.0f, 0.0f, -1.0f)
            };

            HashSet<Vector3> goodPoints = new HashSet<Vector3>();
            int layerMask = 1 << 8;
            int stepsTaken = 0;
            while (pointsQueue.Count != 0) {
                stepsTaken += 1;
                Vector3 p = pointsQueue.Dequeue();
                if (!goodPoints.Contains(p)) {
                    goodPoints.Add(p);
                    HashSet<Collider> objectsAlreadyColliding = new HashSet<Collider>(objectsCollidingWithAgent());
                    foreach (Vector3 d in directions) {
                        RaycastHit[] hits = capsuleCastAllForAgent(
                            cc,
                            sw,
                            p,
                            d,
                            (gridSize * gridMultiplier),
                            layerMask
                        );

                        bool shouldEnqueue = true;
                        foreach (RaycastHit hit in hits) {
                            if (hit.transform.gameObject.name != "Floor" &&
                                !ancestorHasName(hit.transform.gameObject, "FPSController") &&
                                !objectsAlreadyColliding.Contains(hit.collider)
                            ) {
                                shouldEnqueue = false;
                                break;
                            }
                        }
                        Vector3 newPosition = p + d * gridSize * gridMultiplier;
                        bool inBounds = sceneBounds.Contains(newPosition);
                        if (errorMessage == "" && !inBounds) {
                            errorMessage = "In " +
                                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name +
                                ", position " + newPosition.ToString() +
                                " can be reached via capsule cast but is beyond the scene bounds.";
                        }

                        shouldEnqueue = shouldEnqueue && inBounds && (
                            handObjectCanFitInPosition(newPosition, 0.0f) ||
                            handObjectCanFitInPosition(newPosition, 90.0f) ||
                            handObjectCanFitInPosition(newPosition, 180.0f) ||
                            handObjectCanFitInPosition(newPosition, 270.0f)
                        );
                        if (shouldEnqueue) {
                            pointsQueue.Enqueue(newPosition);

                            if (visualize) {
                                var gridRenderer = Instantiate(GridRenderer, Vector3.zero, Quaternion.identity);
                                var gridLineRenderer = gridRenderer.GetComponentInChildren<LineRenderer>();
                                if (gridColor.HasValue) {
                                    gridLineRenderer.startColor = gridColor.Value;
                                    gridLineRenderer.endColor =  gridColor.Value;
                                }
                                // gridLineRenderer.startColor = ;
                                // gridLineRenderer.endColor = ;
                                gridLineRenderer.positionCount = 2;
                                // gridLineRenderer.startWidth = 0.01f;
                                // gridLineRenderer.endWidth = 0.01f;
                                gridLineRenderer.SetPositions(new Vector3[] {
                                    new Vector3(p.x, gridVisualizeY, p.z),
                                    new Vector3(newPosition.x, gridVisualizeY, newPosition.z)
                                });
                            }
                            #if UNITY_EDITOR
                            Debug.DrawLine(p, newPosition, Color.cyan, 100000f);
                            #endif
                        }
                    }
                }
                //default maxStepCount to scale based on gridSize
                if (stepsTaken > Math.Floor(maxStepCount/(gridSize * gridSize))) {
                    errorMessage = "Too many steps taken in GetReachablePositions.";
                    break;
                }
            }

            Vector3[] reachablePos = new Vector3[goodPoints.Count];
            goodPoints.CopyTo(reachablePos);

            #if UNITY_EDITOR
            Debug.Log("count of reachable positions: " + reachablePos.Length);
            #endif

            return reachablePos;
        }

        public void GetReachablePositions(ServerAction action) {
            if(action.maxStepCount != 0) {
                reachablePositions = getReachablePositions(1.0f, action.maxStepCount);
            } else {
                reachablePositions = getReachablePositions();
            }

            if (errorMessage != "") {
                actionFinished(false);
            } else {
                actionFinished(true, reachablePositions);
            }
        }

		public void Initialize(ServerAction action)
        {
            if(action.agentMode.ToLower() == "default" ||
               action.agentMode.ToLower() == "bot" ||
               action.agentMode.ToLower() == "drone")
            {
                //set agent mode to Default, Bot or Drone accordingly
                SetAgentMode(action.agentMode);
            }

            else
            {
                errorMessage = "agentMode must be set to 'default' or 'bot' or 'drone'";
                Debug.Log(errorMessage);
                actionFinished(false);
                return;
            }

            if (action.gridSize == 0)
            {
                action.gridSize = 0.25f;
            }

			if (action.fieldOfView > 0 && action.fieldOfView < 180) {
				m_Camera.fieldOfView = action.fieldOfView;
			}
            else if(action.fieldOfView < 0 || action.fieldOfView >= 180) {
				errorMessage = "fov must be set to (0, 180) noninclusive.";
                Debug.Log(errorMessage);
                actionFinished(false);
                return;
			}

			if (action.timeScale > 0) {
				if (Time.timeScale != action.timeScale) {
                	Time.timeScale = action.timeScale;
				}
            } else {
                errorMessage = "Time scale must be >0";
                Debug.Log(errorMessage);
                actionFinished(false);
                return;
            }

            if (action.rotateStepDegrees <= 0.0)
            {
                errorMessage = "rotateStepDegrees must be a non-zero, non-negative float";
                Debug.Log(errorMessage);
                actionFinished(false);
                return;
            }

            //default is 90 defined in the ServerAction class, specify whatever you want the default to be
            if (action.rotateStepDegrees > 0.0) {
                this.rotateStepDegrees = action.rotateStepDegrees;
            }

            this.snapToGrid = action.snapToGrid;

            if (action.renderDepthImage || action.renderClassImage || action.renderObjectImage || action.renderNormalsImage)
            {
    			this.updateImageSynthesis(true);
    		}

			if (action.visibilityDistance > 0.0f) {
				this.maxVisibleDistance = action.visibilityDistance;
			}

            if (action.gridSize <= 0 || action.gridSize > 5)
            {
                errorMessage = "grid size must be in the range (0,5]";
                Debug.Log(errorMessage);
                actionFinished(false);
                return;
            }
            else
            {
                gridSize = action.gridSize;
                StartCoroutine(checkInitializeAgentLocationAction());
            }

            // Debug.Log("Object " + action.controllerInitialization.ToString() + " dict "  + (action.controllerInitialization.variableInitializations == null));//+ string.Join(";", action.controllerInitialization.variableInitializations.Select(x => x.Key + "=" + x.Value).ToArray()));

            if (action.controllerInitialization != null && action.controllerInitialization.variableInitializations != null) {
                foreach (KeyValuePair<string, TypedVariable> entry in action.controllerInitialization.variableInitializations) {
                    Debug.Log(" Key " + entry.Value.type + " field " + entry.Key);
                    Type t = Type.GetType(entry.Value.type);
                    FieldInfo field = t.GetField(entry.Key, BindingFlags.Public | BindingFlags.Instance);
                    field.SetValue(this, entry.Value);
                }

            }
        }

        public void SetAgentMode(string mode)
        {
            string whichMode;
            whichMode = mode.ToLower();

            //null check for camera, used to ensure no missing references on initialization
            if(m_Camera == null)
            {
                m_Camera = this.gameObject.GetComponentInChildren<Camera>();
            }

            FirstPersonCharacterCull fpcc = m_Camera.GetComponent<FirstPersonCharacterCull>();

            //determine if we are in Tall or Bot mode (or other modes as we go on)
            if(whichMode == "default")
            {
                //toggle FirstPersonCharacterCull
                fpcc.SwitchRenderersToHide(whichMode);

                VisibilityCapsule = TallVisCap;
                m_CharacterController.center = new Vector3(0, 0, 0);
                m_CharacterController.radius = 0.2f;
                m_CharacterController.height = 1.8f;

                CapsuleCollider cc = this.GetComponent<CapsuleCollider>();
                cc.center = m_CharacterController.center;
                cc.radius = m_CharacterController.radius;
                cc.height = m_CharacterController.height;

                m_Camera.GetComponent<PostProcessVolume>().enabled = false;
                m_Camera.GetComponent<PostProcessLayer>().enabled = false;

                //camera position
                m_Camera.transform.localPosition = new Vector3(0, 0.675f, 0);

                //camera FOV
                m_Camera.fieldOfView = 90f;

                //set camera stand/crouch local positions for Tall mode
                standingLocalCameraPosition = m_Camera.transform.localPosition;
                crouchingLocalCameraPosition = m_Camera.transform.localPosition + new Vector3(0, -0.675f, 0);// bigger y offset if tall
            }

            else if(whichMode == "bot")
            {
                //toggle FirstPersonCharacterCull
                fpcc.SwitchRenderersToHide(whichMode);

                VisibilityCapsule = BotVisCap;
                m_CharacterController.center = new Vector3(0, -0.45f, 0);
                m_CharacterController.radius = 0.175f;
                m_CharacterController.height = 0.9f;

                CapsuleCollider cc = this.GetComponent<CapsuleCollider>();
                cc.center = m_CharacterController.center;
                cc.radius = m_CharacterController.radius;
                cc.height = m_CharacterController.height;

                m_Camera.GetComponent<PostProcessVolume>().enabled = true;
                m_Camera.GetComponent<PostProcessLayer>().enabled = true;

                //camera position
                m_Camera.transform.localPosition = new Vector3(0, -0.0312f, 0);

                //camera FOV
                m_Camera.fieldOfView = 60f;

                //set camera stand/crouch local positions for Tall mode
                standingLocalCameraPosition = m_Camera.transform.localPosition;
                crouchingLocalCameraPosition = m_Camera.transform.localPosition + new Vector3(0, -0.2206f, 0);//smaller y offset if Bot

                // limit camera from looking too far down
				this.maxDownwardLookAngle = 30f;
				this.maxUpwardLookAngle = 30f;
                //this.horizonAngles = new float[] { 30.0f, 0.0f, 330.0f };
            }

            else if(whichMode == "drone")
            {
                //toggle first person character cull
                fpcc.SwitchRenderersToHide(whichMode);

                VisibilityCapsule = DroneVisCap;
                m_CharacterController.center = new Vector3(0,0,0);
                m_CharacterController.radius = 0.2f;
                m_CharacterController.height = 0.0f;

                CapsuleCollider cc = this.GetComponent<CapsuleCollider>();
                cc.center = m_CharacterController.center;
                cc.radius = m_CharacterController.radius;
                cc.height = m_CharacterController.height;

                m_Camera.GetComponent<PostProcessVolume>().enabled = false;
                m_Camera.GetComponent<PostProcessLayer>().enabled = false;

                //camera position set forward a bit for drone
                m_Camera.transform.localPosition = new Vector3(0, 0, 0.2f);

                //camera FOV for drone
                m_Camera.fieldOfView = 150f;

                //default camera stand/crouch for drone mode since drone doesn't stand or crouch
                standingLocalCameraPosition = m_Camera.transform.localPosition;
                crouchingLocalCameraPosition = m_Camera.transform.localPosition;

                //drone also needs to toggle on the drone basket
                DroneBasket.SetActive(true);
            }
        }

        public IEnumerator checkInitializeAgentLocationAction()
        {
            yield return null;

            Vector3 startingPosition = this.transform.position;
            // move ahead
            // move back

            float mult = 1 / gridSize;
            float grid_x1 = Convert.ToSingle(Math.Floor(this.transform.position.x * mult) / mult);
            float grid_z1 = Convert.ToSingle(Math.Floor(this.transform.position.z * mult) / mult);

            float[] xs = new float[] { grid_x1, grid_x1 + gridSize };
            float[] zs = new float[] { grid_z1, grid_z1 + gridSize };
            List<Vector3> validMovements = new List<Vector3>();

            foreach (float x in xs)
            {
                foreach (float z in zs)
                {
                    this.transform.position = startingPosition;
                    yield return null;

                    Vector3 target = new Vector3(x, this.transform.position.y, z);
                    Vector3 dir = target - this.transform.position;
                    Vector3 movement = dir.normalized * 100.0f;
                    if (movement.magnitude > dir.magnitude)
                    {
                        movement = dir;
                    }

                    movement.y = Physics.gravity.y * this.m_GravityMultiplier;

                    m_CharacterController.Move(movement);

                    for (int i = 0; i < actionDuration; i++)
                    {
                        yield return null;
                        Vector3 diff = this.transform.position - target;


                        if ((Math.Abs(diff.x) < 0.005) && (Math.Abs(diff.z) < 0.005))
                        {
                            validMovements.Add(movement);
                            break;
                        }
                    }

                }
            }

            this.transform.position = startingPosition;
            yield return null;
            if (validMovements.Count > 0)
            {
                Debug.Log("Initialize: got total valid initial targets: " + validMovements.Count);
                Vector3 firstMove = validMovements[0];
                firstMove.y = Physics.gravity.y * this.m_GravityMultiplier;

                m_CharacterController.Move(firstMove);
                snapAgentToGrid();
                actionFinished(true, new InitializeReturn{
                    cameraNearPlane = m_Camera.nearClipPlane,
                    cameraFarPlane = m_Camera.farClipPlane
                });
            }

            else
            {
                Debug.Log("Initialize: no valid starting positions found");
                actionFinished(false);
            }
        }

		public bool excludeObject(string objectId)
		{
			return Array.IndexOf(this.excludeObjectIds, objectId) >= 0;
		}

        // //currently unused
		// public bool excludeObject(SimpleSimObj so)
		// {
		// 	return excludeObject(so.ObjectID);
		// }

        // //currently unused
		// protected bool closeSimObj(SimpleSimObj so)
		// {
		// 	return so.Close();
		// }

        // //currently unused
		// protected bool openSimObj(SimpleSimObj so)
		// {
		// 	return so.Open();
		// }

        //for all translational movement, check if the item the player is holding will hit anything, or if the agent will hit anything
        //NOTE: (XXX) All four movements below no longer use base character controller Move() due to doing initial collision blocking
        //checks before actually moving. Previously we would moveCharacter() first and if we hit anything reset, but now to match
        //Luca's movement grid and valid position generation, simple transform setting is used for movement instead.

        //XXX revisit what movement means when we more clearly define what "continuous" movement is
        protected bool moveInDirection(Vector3 direction, string objectId="", float maxDistanceToObject=-1.0f, bool forceAction = false) {
			Console.WriteLine("BaseFPSAgentController.moveInDirection reached for objectId " + objectId);

            Vector3 targetPosition = transform.position + direction;
            float angle = Vector3.Angle(transform.forward, Vector3.Normalize(direction));

            float right = Vector3.Dot(transform.right, direction);
            if (right < 0) {
                angle = 360f - angle;
            }
            int angleInt = Mathf.RoundToInt(angle) % 360;
			bool status;

            if (checkIfSceneBoundsContainTargetPosition(targetPosition) &&
                CheckIfItemBlocksAgentMovement(direction.magnitude, angleInt, forceAction) && // forceAction = true allows ignoring movement restrictions caused by held objects
                CheckIfAgentCanMove(direction.magnitude, angleInt)) {
                DefaultAgentHand();
                Vector3 oldPosition = transform.position;
                transform.position = targetPosition;
                this.snapAgentToGrid();

                if (objectId != "" && maxDistanceToObject > 0.0f) {
                    if (!physicsSceneManager.ObjectIdToSimObjPhysics.ContainsKey(objectId)) {
                        errorMessage = "No object with ID " + objectId;
                        transform.position = oldPosition;
                        return false;
                    }
                    SimObjPhysics sop = physicsSceneManager.ObjectIdToSimObjPhysics[objectId];
                    if (distanceToObject(sop) > maxDistanceToObject) {
                        errorMessage = "Agent movement would bring it beyond the max distance of " + objectId;
                        transform.position = oldPosition;
                        return false;
                    }
                }
                status = true;
            } else {
                status = false;
            }

            #if UNITY_WEBGL
            JavaScriptInterface jsInterface = this.GetComponent<JavaScriptInterface>();
            if (jsInterface != null) {
                MovementWrapper movement = new MovementWrapper();
                movement.position = transform.position;
				movement.rotationEulerAngles = transform.rotation.eulerAngles;
				movement.direction = direction;
				movement.targetPosition = targetPosition;
				movement.angle = angle;
				movement.angleInt = angleInt;
				movement.succeeded = status;

                jsInterface.SendMovementData(movement);
			}
		    #endif

			return status;
        }

        protected float distanceToObject(SimObjPhysics sop) {
            float dist = 10000.0f;
            foreach (Collider c in sop.GetComponentsInChildren<Collider>()) {
                Vector3 closestPoint = c.ClosestPointOnBounds(transform.position);
                Vector3 p0 = new Vector3(transform.position.x, 0f, transform.position.z);
                Vector3 p1 = new Vector3(closestPoint.x, 0f, closestPoint.z);
                dist = Math.Min(Vector3.Distance(p0, p1), dist);
            }
            return dist;
        }

        public void DistanceToObject(ServerAction action) {
            float dist = distanceToObject(physicsSceneManager.ObjectIdToSimObjPhysics[action.objectId]);
            #if UNITY_EDITOR
            Debug.Log(dist);
            #endif
            actionFinished(true, dist);
        }

        public bool CheckIfAgentCanMove(float moveMagnitude, int orientation) {
            Vector3 dir = new Vector3();

            switch (orientation) {
                case 0: //forward
                    dir = gameObject.transform.forward;
                    break;

                case 180: //backward
                    dir = -gameObject.transform.forward;
                    break;

                case 270: //left
                    dir = -gameObject.transform.right;
                    break;

                case 90: //right
                    dir = gameObject.transform.right;
                    break;

                default:
                    Debug.Log("Incorrect orientation input! Allowed orientations (0 - forward, 90 - right, 180 - backward, 270 - left) ");
                    break;
            }

            RaycastHit[] sweepResults = capsuleCastAllForAgent(
                GetComponent<CapsuleCollider>(),
                m_CharacterController.skinWidth,
                transform.position,
                dir,
                moveMagnitude,
                1 << 8 | 1 << 10
            );
            //check if we hit an environmental structure or a sim object that we aren't actively holding. If so we can't move
            if (sweepResults.Length > 0) {
                foreach (RaycastHit res in sweepResults) {
                    // Don't worry if we hit something thats in our hand.
                    if (ItemInHand != null && ItemInHand.transform == res.transform) {
                        continue;
                    }

                    if (res.transform.gameObject != this.gameObject && res.transform.GetComponent<PhysicsRemoteFPSAgentController>()) {

                        PhysicsRemoteFPSAgentController maybeOtherAgent = res.transform.GetComponent<PhysicsRemoteFPSAgentController>();
                        int thisAgentNum = agentManager.agents.IndexOf(this);
                        int otherAgentNum = agentManager.agents.IndexOf(maybeOtherAgent);
                        errorMessage = "Agent " + otherAgentNum.ToString() + " is blocking Agent " + thisAgentNum.ToString() + " from moving " + orientation;
                        return false;
                    }

                    //including "Untagged" tag here so that the agent can't move through objects that are transparent
                    if (res.transform.GetComponent<SimObjPhysics>() || res.transform.tag == "Structure" || res.transform.tag == "Untagged") {
                        int thisAgentNum = agentManager.agents.IndexOf(this);
                        errorMessage = res.transform.name + " is blocking Agent " + thisAgentNum.ToString() + " from moving " + orientation;
                        //the moment we find a result that is blocking, return false here
                        return false;
                    }
                }
            }
            return true;
        }

        public void DisableObject(ServerAction action) {
            string objectId = action.objectId;
            if (physicsSceneManager.ObjectIdToSimObjPhysics.ContainsKey(objectId)) {
                physicsSceneManager.ObjectIdToSimObjPhysics[objectId].gameObject.SetActive(false);
                actionFinished(true);
            } else {
                actionFinished(false);
            }
        }

        public void EnableObject(ServerAction action) {
            string objectId = action.objectId;
            if (physicsSceneManager.ObjectIdToSimObjPhysics.ContainsKey(objectId)) {
                physicsSceneManager.ObjectIdToSimObjPhysics[objectId].gameObject.SetActive(true);
                actionFinished(true);
            } else {
                actionFinished(false);
            }
        }

        //remove a given sim object from the scene. Pass in the object's objectID string to remove it.
        public void RemoveFromScene(ServerAction action) {
            //pass name of object in from action.objectId
            if (action.objectId == null) {
                errorMessage = "objectId required for OpenObject";
                actionFinished(false);
                return;
            }

            //see if the object exists in this scene
            if (physicsSceneManager.ObjectIdToSimObjPhysics.ContainsKey(action.objectId)) {
                physicsSceneManager.ObjectIdToSimObjPhysics[action.objectId].transform.gameObject.SetActive(false);
                physicsSceneManager.SetupScene();
                actionFinished(true);
                return;
            }

            errorMessage = action.objectId + " could not be found in this scene, so it can't be removed";
            actionFinished(false);
        }

        //Sweeptest to see if the object Agent is holding will prohibit movement
        public bool CheckIfItemBlocksAgentMovement(float moveMagnitude, int orientation, bool forceAction = false) {
            bool result = false;

            //if forceAction true, ignore collision restrictions caused by held objects
            if(forceAction)
            {
                return true;
            }
            //if there is nothing in our hand, we are good, return!
            if (ItemInHand == null) {
                result = true;
                //  Debug.Log("Agent has nothing in hand blocking movement");
                return result;
            }

            //otherwise we are holding an object and need to do a sweep using that object's rb
            else {
                Vector3 dir = new Vector3();

                //use the agent's forward as reference
                switch (orientation) {
                    case 0: //forward
                        dir = gameObject.transform.forward;
                        break;

                    case 180: //backward
                        dir = -gameObject.transform.forward;
                        break;

                    case 270: //left
                        dir = -gameObject.transform.right;
                        break;

                    case 90: //right
                        dir = gameObject.transform.right;
                        break;

                    default:
                        Debug.Log("Incorrect orientation input! Allowed orientations (0 - forward, 90 - right, 180 - backward, 270 - left) ");
                        break;
                }
                //otherwise we haev an item in our hand, so sweep using it's rigid body.
                //RaycastHit hit;

                Rigidbody rb = ItemInHand.GetComponent<Rigidbody>();

                RaycastHit[] sweepResults = rb.SweepTestAll(dir, moveMagnitude, QueryTriggerInteraction.Ignore);
                if (sweepResults.Length > 0) {
                    foreach (RaycastHit res in sweepResults) {
                        //did the item in the hand touch the agent? if so, ignore it's fine
                        if (res.transform.tag == "Player") {
                            result = true;
                            break;
                        } else {
                            errorMessage = res.transform.name + " is blocking the Agent from moving " + orientation + " with " + ItemInHand.name;
                            result = false;
                            Debug.Log(errorMessage);
                            return result;
                        }

                    }
                }

                //if the array is empty, nothing was hit by the sweeptest so we are clear to move
                else {
                    //Debug.Log("Agent Body can move " + orientation);
                    result = true;
                }

                return result;
            }
        }

        public bool checkIfSceneBoundsContainTargetPosition(Vector3 position) {
            if (!sceneBounds.Contains(position)) {
                errorMessage = "Scene bounds do not contain target position: " + position;
                return false;
            } else {
                return true;
            }
        }

        //if you want to do something like throw objects to knock over other objects, use this action to set all objects to Kinematic false
        //otherwise objects will need to be hit multiple times in order to ensure kinematic false toggle
        //use this by initializing the scene, then calling randomize if desired, and then call this action to prepare the scene so all objects will react to others upon collision.
        //note that SOMETIMES rigidbodies will continue to jitter or wiggle, especially if they are stacked against other rigidbodies.
        //this means that the isSceneAtRest bool will always be false
        public void MakeAllObjectsMoveable(ServerAction action)
        {
            foreach (SimObjPhysics sop in GameObject.FindObjectsOfType<SimObjPhysics>())
            {
                //check if the sopType is something that can be hung
                if(sop.Type == SimObjType.Towel || sop.Type == SimObjType.HandTowel || sop.Type == SimObjType.ToiletPaper)
                {
                    //if this object is actively hung on its corresponding object specific receptacle... skip it so it doesn't fall on the floor
                    if(sop.GetComponentInParent<ObjectSpecificReceptacle>())
                    {
                        continue;
                    }
                }

                if (sop.PrimaryProperty == SimObjPrimaryProperty.CanPickup || sop.PrimaryProperty == SimObjPrimaryProperty.Moveable)
                {
                    Rigidbody rb = sop.GetComponent<Rigidbody>();
                    rb.isKinematic = false;
                    rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                }
            }
            actionFinished(true);
        }

        //this does not appear to be used except for by the python unit test?
        //May deprecate this at some point?
		public void RotateLook(ServerAction response)
		{
			transform.rotation = Quaternion.Euler(new Vector3(0.0f, response.rotation.y, 0.0f));
			m_Camera.transform.localEulerAngles = new Vector3(response.horizon, 0.0f, 0.0f);
			actionFinished(true);

		}

		// rotate view with respect to mouse or server controls - I'm not sure when this is actually used
		protected virtual void RotateView()
		{
			// turn up & down
			if (Mathf.Abs(m_XRotation) > Mathf.Epsilon)
			{
				transform.Rotate(Vector3.right * m_XRotation, Space.Self);
			}

			// turn left & right
			if (Mathf.Abs(m_ZRotation) > Mathf.Epsilon)
			{
				transform.Rotate(Vector3.up * m_ZRotation, Space.Self);
			}

			// heading
			float eulerX = Mathf.Round(transform.eulerAngles.x);

			// rotating
			float eulerY = Mathf.Round(transform.eulerAngles.y);

			// TODO: make this as a precondition
			// move this out of Unity
			// constrain vertical turns in safe range
			float X_SAFE_RANGE = 30.0f;
			if (eulerX < 180.0f)
			{
				eulerX = Mathf.Min(X_SAFE_RANGE, eulerX);
			}
			else
			{
				eulerX = 360.0f - Mathf.Min(X_SAFE_RANGE, 360.0f - eulerX);
			}

			// freeze y-axis
			transform.rotation = Quaternion.Euler(eulerX, eulerY, 0);

		}

		// Check if agent is collided with other objects
		protected bool IsCollided()
		{
			return collisionsInAction.Count > 0;
		}

        public virtual SimpleSimObj[] allSceneObjects() {
			return GameObject.FindObjectsOfType<SimObj>();
        }

        public virtual ObjectMetadata[] generateObjectMetadata()
		{
            SimObjPhysics[] visibleSimObjs = VisibleSimObjs(false); // Update visibility for all sim objects for this agent
            HashSet<SimObjPhysics> visibleSimObjsHash = new HashSet<SimObjPhysics>();
            foreach (SimObjPhysics sop in visibleSimObjs) {
                visibleSimObjsHash.Add(sop);
            }

            // Encode these in a json string and send it to the server
            SimObjPhysics[] simObjects = GameObject.FindObjectsOfType<SimObjPhysics>();
            int numObj = simObjects.Length;
            List<ObjectMetadata> metadata = new List<ObjectMetadata>();
            Dictionary<string, List<string>> parentReceptacles = new Dictionary<string, List<string>>();

            #if UNITY_EDITOR
            //debug draw bounds reset list
            gizmobounds.Clear();
            #endif

            for (int k = 0; k < numObj; k++) {
                SimObjPhysics simObj = simObjects[k];
                if (this.excludeObject(simObj.ObjectID)) {
                    continue;
                }
                ObjectMetadata meta = ObjectMetadataFromSimObjPhysics(simObj, visibleSimObjsHash.Contains(simObj));
                if (meta.receptacle) {
                    List<string> roid = simObj.Contains();
                    foreach (string oid in roid) {
                        if (!parentReceptacles.ContainsKey(oid)) {
                            parentReceptacles[oid] = new List<string>();
                        }
                        parentReceptacles[oid].Add(simObj.ObjectID);
                    }
                    meta.receptacleObjectIds = roid.ToArray();
                }
                meta.distance = Vector3.Distance(transform.position, simObj.gameObject.transform.position);
                metadata.Add(meta);
            }
            foreach (ObjectMetadata meta in metadata) {
                if (parentReceptacles.ContainsKey(meta.objectId)) {
                    meta.parentReceptacles = parentReceptacles[meta.objectId].ToArray();
                }
            }
            return metadata.ToArray();
		}

        //generates object metatada based on sim object's properties
        public virtual ObjectMetadata ObjectMetadataFromSimObjPhysics(SimObjPhysics simObj, bool isVisible) {
            ObjectMetadata objMeta = new ObjectMetadata();
            GameObject o = simObj.gameObject;
            objMeta.name = o.name;
            objMeta.position = o.transform.position;
            objMeta.rotation = o.transform.eulerAngles;
            objMeta.objectType = Enum.GetName(typeof(SimObjType), simObj.Type);
            objMeta.receptacle = simObj.IsReceptacle;

            objMeta.openable = simObj.IsOpenable;
            if (objMeta.openable) {
                objMeta.isOpen = simObj.IsOpen;
            }

            objMeta.toggleable = simObj.IsToggleable;
            if (objMeta.toggleable) {
                objMeta.isToggled = simObj.IsToggled;
            }

            objMeta.breakable = simObj.IsBreakable;
            if(objMeta.breakable) {
                objMeta.isBroken = simObj.IsBroken;
            }

            objMeta.canFillWithLiquid = simObj.IsFillable;
            if (objMeta.canFillWithLiquid) {
                objMeta.isFilledWithLiquid = simObj.IsFilled;
            }

            objMeta.dirtyable = simObj.IsDirtyable;
            if (objMeta.dirtyable) {
                objMeta.isDirty = simObj.IsDirty;
            }

            objMeta.cookable = simObj.IsCookable;
            if (objMeta.cookable) {
                objMeta.isCooked = simObj.IsCooked;
            }

            //if the sim object is moveable or pickupable
            if(simObj.IsPickupable || simObj.IsMoveable || simObj.salientMaterials.Length > 0)
            {
                //this object should report back mass and salient materials

                string [] salientMaterialsToString = new string [simObj.salientMaterials.Length];

                for(int i = 0; i < simObj.salientMaterials.Length; i++)
                {
                    salientMaterialsToString[i] = simObj.salientMaterials[i].ToString();
                }

                objMeta.salientMaterials = salientMaterialsToString;

                //this object should also report back mass since it is moveable/pickupable
                objMeta.mass = simObj.Mass;

            }

            //can this object change others to hot?
            objMeta.canChangeTempToHot = simObj.canChangeTempToHot;

            //can this object change others to cold?
            objMeta.canChangeTempToCold = simObj.canChangeTempToCold;

            //placeholder for heatable objects -kettle, pot, pan
            // objMeta.abletocook = simObj.abletocook;
            // if(objMeta.abletocook) {
            //     objMeta.isReadyToCook = simObj.IsHeated;
            // }

            objMeta.sliceable = simObj.IsSliceable;
            if (objMeta.sliceable) {
                objMeta.isSliced = simObj.IsSliced;
            }

            objMeta.canBeUsedUp = simObj.CanBeUsedUp;
            if (objMeta.canBeUsedUp) {
                objMeta.isUsedUp = simObj.IsUsedUp;
            }

            //object temperature to string
            objMeta.ObjectTemperature = simObj.CurrentObjTemp.ToString();

            objMeta.pickupable = simObj.PrimaryProperty == SimObjPrimaryProperty.CanPickup;//can this object be picked up?
            objMeta.isPickedUp = simObj.isPickedUp;//returns true for if this object is currently being held by the agent

            objMeta.moveable = simObj.PrimaryProperty == SimObjPrimaryProperty.Moveable;

            objMeta.objectId = simObj.ObjectID;

            // TODO: using the isVisible flag on the object causes weird problems
            // in the multiagent setting, explicitly giving this information for now.
            objMeta.visible = isVisible; //simObj.isVisible;

            objMeta.isMoving = simObj.inMotion;//keep track of if this object is actively moving

            if(simObj.PrimaryProperty == SimObjPrimaryProperty.CanPickup || simObj.PrimaryProperty == SimObjPrimaryProperty.Moveable)
            {
                objMeta.objectOrientedBoundingBox = GenerateObjectOrientedBoundingBox(simObj);
            }

            //return world axis aligned bounds for this sim object
            objMeta.axisAlignedBoundingBox = GenerateAxisAlignedBoundingBox(simObj);

            return objMeta;
        }

        //generates an object oriented bounding box that encapsulates the sim object
        //currently only works for Pickupable sim objects
        public ObjectOrientedBoundingBox GenerateObjectOrientedBoundingBox(SimObjPhysics sop)
        {
            ObjectOrientedBoundingBox b = new ObjectOrientedBoundingBox();

            if(sop.BoundingBox== null)
            {
                Debug.LogError(sop.transform.name + " is missing BoundingBox reference!");
                return b;
            }

            BoxCollider col = sop.BoundingBox.GetComponent<BoxCollider>();

            Vector3 p0 = col.transform.TransformPoint(col.center + new Vector3(col.size.x, -col.size.y, col.size.z) * 0.5f);
            Vector3 p1 = col.transform.TransformPoint(col.center + new Vector3(-col.size.x, -col.size.y, col.size.z) * 0.5f);
            Vector3 p2 = col.transform.TransformPoint(col.center + new Vector3(-col.size.x, -col.size.y, -col.size.z) * 0.5f);
            Vector3 p3 = col.transform.TransformPoint(col.center + new Vector3(col.size.x, -col.size.y, -col.size.z) * 0.5f);
            Vector3 p4 = col.transform.TransformPoint(col.center + new Vector3(col.size.x, col.size.y, col.size.z) * 0.5f);
            Vector3 p5 = col.transform.TransformPoint(col.center + new Vector3(-col.size.x, col.size.y, col.size.z) * 0.5f);
            Vector3 p6 = col.transform.TransformPoint(col.center + new Vector3(-col.size.x, +col.size.y, -col.size.z) * 0.5f);
            Vector3 p7 = col.transform.TransformPoint(col.center + new Vector3(col.size.x, col.size.y, -col.size.z) * 0.5f);

            b.cornerPoints[0,0] = p0.x;
            b.cornerPoints[0,1] = p0.y;
            b.cornerPoints[0,2] = p0.z;

            b.cornerPoints[1,0] = p1.x;
            b.cornerPoints[1,1] = p1.y;
            b.cornerPoints[1,2] = p1.z;

            b.cornerPoints[2,0] = p2.x;
            b.cornerPoints[2,1] = p2.y;
            b.cornerPoints[2,2] = p2.z;

            b.cornerPoints[3,0] = p3.x;
            b.cornerPoints[3,1] = p3.y;
            b.cornerPoints[3,2] = p3.z;

            b.cornerPoints[4,0] = p4.x;
            b.cornerPoints[4,1] = p4.y;
            b.cornerPoints[4,2] = p4.z;

            b.cornerPoints[5,0] = p5.x;
            b.cornerPoints[5,1] = p5.y;
            b.cornerPoints[5,2] = p5.z;

            b.cornerPoints[6,0] = p6.x;
            b.cornerPoints[6,1] = p6.y;
            b.cornerPoints[6,2] = p6.z;

            b.cornerPoints[7,0] = p7.x;
            b.cornerPoints[7,1] = p7.y;
            b.cornerPoints[7,2] = p7.z;

            return b;
        }

        public SceneBounds GenerateSceneBounds(Bounds bounding)
        {
            SceneBounds b = new SceneBounds();

            b.cornerPoints[0,0] = bounding.center.x + bounding.size.x/2f;
            b.cornerPoints[0,1] = bounding.center.y + bounding.size.y/2f;
            b.cornerPoints[0,2] = bounding.center.z + bounding.size.z/2f;

            b.cornerPoints[1,0] = bounding.center.x + bounding.size.x/2f;
            b.cornerPoints[1,1] = bounding.center.y + bounding.size.y/2f;
            b.cornerPoints[1,2] = bounding.center.z - bounding.size.z/2f;

            b.cornerPoints[2,0] = bounding.center.x + bounding.size.x/2f;
            b.cornerPoints[2,1] = bounding.center.y - bounding.size.y/2f;
            b.cornerPoints[2,2] = bounding.center.z + bounding.size.z/2f;

            b.cornerPoints[3,0] = bounding.center.x + bounding.size.x/2f;
            b.cornerPoints[3,1] = bounding.center.y - bounding.size.y/2f;
            b.cornerPoints[3,2] = bounding.center.z - bounding.size.z/2f;

            b.cornerPoints[4,0] = bounding.center.x - bounding.size.x/2f;
            b.cornerPoints[4,1] = bounding.center.y + bounding.size.y/2f;
            b.cornerPoints[4,2] = bounding.center.z + bounding.size.z/2f;

            b.cornerPoints[5,0] = bounding.center.x - bounding.size.x/2f;
            b.cornerPoints[5,1] = bounding.center.y + bounding.size.y/2f;
            b.cornerPoints[5,2] = bounding.center.z - bounding.size.z/2f;

            b.cornerPoints[6,0] = bounding.center.x - bounding.size.x/2f;
            b.cornerPoints[6,1] = bounding.center.y - bounding.size.y/2f;
            b.cornerPoints[6,2] = bounding.center.z + bounding.size.z/2f;

            b.cornerPoints[7,0] = bounding.center.x - bounding.size.x/2f;
            b.cornerPoints[7,1] = bounding.center.y - bounding.size.y/2f;
            b.cornerPoints[7,2] = bounding.center.z - bounding.size.z/2f;

            b.center = bounding.center;
            b.size = bounding.size;

            return b;
        }

        //generates a world space bounding box that enncapsulates all active Colliders (trigger and non trigger) for a sim obj
        public AxisAlignedBoundingBox GenerateAxisAlignedBoundingBox(SimObjPhysics sop)
        {
            AxisAlignedBoundingBox b = new AxisAlignedBoundingBox();

            //get all colliders on the sop, excluding colliders if they are not enabled
            Collider[] cols = sop.GetComponentsInChildren<Collider>();

            //0 colliders mean the object is despawned, so this will cause objects broken into pieces to not generate an axis aligned box
            if(cols.Length == 0)
            {
                if(sop.GetComponent<SimObjPhysics>().IsBroken)
                {
                    #if UNITY_EDITOR
                    Debug.Log("Object is broken in pieces, no AxisAligned box generated: " + sop.name);
                    #endif
                    return b;
                }

                else
                {
                    #if UNITY_EDITOR
                    Debug.Log("Something went wrong, no Colliders were found on" + sop.name);
                    #endif
                    return b;
                }
            }

            Bounds bounding = cols[0].bounds;//initialize the bounds to return with our first collider

            foreach(Collider c in cols)
            {
                if(c.enabled)
                bounding.Encapsulate(c.bounds);
            }

            #if UNITY_EDITOR
            //debug draw stuff
            if(!gizmobounds.Contains(bounding))
            gizmobounds.Add(bounding);
            #endif

            //ok now we have a bounds that encapsulates all the colliders of the object, including trigger colliders
            b.cornerPoints[0,0] = bounding.center.x + bounding.size.x/2f;
            b.cornerPoints[0,1] = bounding.center.y + bounding.size.y/2f;
            b.cornerPoints[0,2] = bounding.center.z + bounding.size.z/2f;

            b.cornerPoints[1,0] = bounding.center.x + bounding.size.x/2f;
            b.cornerPoints[1,1] = bounding.center.y + bounding.size.y/2f;
            b.cornerPoints[1,2] = bounding.center.z - bounding.size.z/2f;

            b.cornerPoints[2,0] = bounding.center.x + bounding.size.x/2f;
            b.cornerPoints[2,1] = bounding.center.y - bounding.size.y/2f;
            b.cornerPoints[2,2] = bounding.center.z + bounding.size.z/2f;

            b.cornerPoints[3,0] = bounding.center.x + bounding.size.x/2f;
            b.cornerPoints[3,1] = bounding.center.y - bounding.size.y/2f;
            b.cornerPoints[3,2] = bounding.center.z - bounding.size.z/2f;

            b.cornerPoints[4,0] = bounding.center.x - bounding.size.x/2f;
            b.cornerPoints[4,1] = bounding.center.y + bounding.size.y/2f;
            b.cornerPoints[4,2] = bounding.center.z + bounding.size.z/2f;

            b.cornerPoints[5,0] = bounding.center.x - bounding.size.x/2f;
            b.cornerPoints[5,1] = bounding.center.y + bounding.size.y/2f;
            b.cornerPoints[5,2] = bounding.center.z - bounding.size.z/2f;

            b.cornerPoints[6,0] = bounding.center.x - bounding.size.x/2f;
            b.cornerPoints[6,1] = bounding.center.y - bounding.size.y/2f;
            b.cornerPoints[6,2] = bounding.center.z + bounding.size.z/2f;

            b.cornerPoints[7,0] = bounding.center.x - bounding.size.x/2f;
            b.cornerPoints[7,1] = bounding.center.y - bounding.size.y/2f;
            b.cornerPoints[7,2] = bounding.center.z - bounding.size.z/2f;

            b.center = bounding.center;//also return the center of this bounding box in world coordinates
            b.size = bounding.size;//also return the size in the x, y, z axes of the bounding box in world coordinates

            return b;
        }
		public virtual MetadataWrapper generateMetadataWrapper()
		{
            // AGENT METADATA
            AgentMetadata agentMeta = new AgentMetadata();
            agentMeta.name = "agent";
            agentMeta.position = transform.position;
            agentMeta.rotation = transform.eulerAngles;
            agentMeta.cameraHorizon = m_Camera.transform.rotation.eulerAngles.x;
            if (agentMeta.cameraHorizon > 180)
            {
                agentMeta.cameraHorizon -= 360;
            }
	        agentMeta.isStanding = (m_Camera.transform.localPosition - standingLocalCameraPosition).magnitude < 0.1f;
            agentMeta.inHighFrictionArea = inHighFrictionArea;

            // OTHER METADATA
            MetadataWrapper metaMessage = new MetadataWrapper();
            metaMessage.agent = agentMeta;
            metaMessage.sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            metaMessage.objects = this.generateObjectMetadata();
            metaMessage.isSceneAtRest = physicsSceneManager.isSceneAtRest;
            metaMessage.sceneBounds = GenerateSceneBounds(sceneBounds);
            metaMessage.collided = collidedObjects.Length > 0;
            metaMessage.collidedObjects = collidedObjects;
            metaMessage.screenWidth = Screen.width;
            metaMessage.screenHeight = Screen.height;
            metaMessage.cameraPosition = m_Camera.transform.position;
            metaMessage.cameraOrthSize = cameraOrthSize;
            cameraOrthSize = -1f;
            metaMessage.fov = m_Camera.fieldOfView;
            metaMessage.lastAction = lastAction;
            metaMessage.lastActionSuccess = lastActionSuccess;
			metaMessage.lastActionObjectId = lastActionObjectId;
            metaMessage.lastActionObjectName = lastActionObjectName;
            metaMessage.lastActionObjectType = lastActionObjectType;
			metaMessage.lastActionRceptacleObjectId = lastActionRceptacleObjectId;
			metaMessage.lastActionReceptacleObjectType = lastActionReceptacleObjectType;
            metaMessage.lastActionX = lastActionX;
            metaMessage.lastActionY = lastActionY;
            metaMessage.lastActionZ = lastActionZ;
            metaMessage.errorMessage = errorMessage;

            //Console.WriteLine(String.Format("In BaseFPSAgentController.generateMetadataWrapper(), metaMessage.lastAction is {0}, metaMessage.lastActionObjectName is {1}",
            //                                                metaMessage.lastAction, metaMessage.lastActionObjectName));

            if (errorCode != ServerActionErrorCode.Undefined) {
                metaMessage.errorCode = Enum.GetName(typeof(ServerActionErrorCode), errorCode);
            }

            List<InventoryObject> ios = new List<InventoryObject>();

            if (ItemInHand != null) {
                SimObjPhysics so = ItemInHand.GetComponent<SimObjPhysics>();
                InventoryObject io = new InventoryObject();
                io.objectId = so.ObjectID;
                io.objectType = Enum.GetName(typeof(SimObjType), so.Type);
                ios.Add(io);
            }

            metaMessage.inventoryObjects = ios.ToArray();

            // HAND
            metaMessage.hand = new HandMetadata();
            metaMessage.hand.position = AgentHand.transform.position;
            metaMessage.hand.localPosition = AgentHand.transform.localPosition;
            metaMessage.hand.rotation = AgentHand.transform.eulerAngles;
            metaMessage.hand.localRotation = AgentHand.transform.localEulerAngles;

            // EXTRAS
            metaMessage.reachablePositions = reachablePositions;
            metaMessage.flatSurfacesOnGrid = flatten3DimArray(flatSurfacesOnGrid);
            metaMessage.distances = flatten2DimArray(distances);
            metaMessage.normals = flatten3DimArray(normals);
            metaMessage.isOpenableGrid = flatten2DimArray(isOpenableGrid);
            metaMessage.segmentedObjectIds = segmentedObjectIds;
            metaMessage.objectIdsInBox = objectIdsInBox;
            metaMessage.actionIntReturn = actionIntReturn;
            metaMessage.actionFloatReturn = actionFloatReturn;
            metaMessage.actionFloatsReturn = actionFloatsReturn;
            metaMessage.actionStringsReturn = actionStringsReturn;
            metaMessage.actionVector3sReturn = actionVector3sReturn;

            if (alwaysReturnVisibleRange) {
                metaMessage.visibleRange = visibleRange();
            }

            //test time
            metaMessage.currentTime = TimeSinceStart();

            // Resetting things
            reachablePositions = new Vector3[0];
            flatSurfacesOnGrid = new float[0, 0, 0];
            distances = new float[0, 0];
            normals = new float[0, 0, 0];
            isOpenableGrid = new bool[0, 0];
            segmentedObjectIds = new string[0];
            objectIdsInBox = new string[0];
            actionIntReturn = 0;
            actionFloatReturn = 0.0f;
            actionFloatsReturn = new float[0];
            actionStringsReturn = new string[0];
            actionVector3sReturn = new Vector3[0];

            return metaMessage;
		}

		// public virtual SimpleSimObj[] VisibleSimObjs() {
		// 	return new SimObj[]{} as SimpleSimObj[];
		// }

		public void updateImageSynthesis(bool status) {
            if (this.imageSynthesis == null) {
                imageSynthesis = this.gameObject.GetComponentInChildren<ImageSynthesis> () as ImageSynthesis;
            }
			imageSynthesis.enabled = status;
		}

		public void ProcessControlCommand(ServerAction controlCommand)
		{
            currentServerAction = controlCommand;

	        errorMessage = "";
			errorCode = ServerActionErrorCode.Undefined;
			collisionsInAction = new List<string>();

			lastAction = controlCommand.action;
			lastActionSuccess = false;
			lastActionObjectId = controlCommand.objectId;
            lastActionObjectName = controlCommand.objectName;
			lastActionObjectType = controlCommand.objectType;
			lastActionRceptacleObjectId = controlCommand.receptacleObjectId;
			lastActionReceptacleObjectType = controlCommand.receptacleObjectType;
            lastActionX = controlCommand.x;
            lastActionY = controlCommand.y;
            lastActionZ = controlCommand.z;

            //Console.WriteLine(String.Format("In BaseFPSAgentController.ProcessControlCommand(), lastAction is {0}, lastActionObjectName is {1}",
            //                                                lastAction, lastActionObjectName));

            lastPosition = new Vector3(transform.position.x, transform.position.y, transform.position.z);
			System.Reflection.MethodInfo method = this.GetType().GetMethod(controlCommand.action);

			this.actionComplete = false;
			try
			{
				if (method == null) {
					errorMessage = "Invalid action: " + controlCommand.action;
					errorCode = ServerActionErrorCode.InvalidAction;
					Debug.LogError(errorMessage);
					actionFinished(false);
				} else {
					method.Invoke(this, new object[] { controlCommand });
				}
			}
			catch (Exception e)
			{
				Debug.LogError("Caught error with invoke for action: " + controlCommand.action);
                Debug.LogError("Action error message: " + errorMessage);
				Debug.LogError(e);

				errorMessage += e.ToString();
				actionFinished(false);
			}

            #if UNITY_EDITOR
			if (errorMessage != "") {
				Debug.Log(errorMessage);
			}
            #endif

			agentManager.setReadyToEmit(true);
		}

        //no op action
        public void Pass(ServerAction action) {
            actionFinished(true);
        }

		// Handle collisions - CharacterControllers don't apply physics innately, see "PushMode" check below
        // XXX: this will be used for truly continuous movement over time, for now this is unused
		protected void OnControllerColliderHit(ControllerColliderHit hit)
		{
			if (!enabled)
			{
				return;
			}

			if (hit.gameObject.GetComponent<StructureObject>())
			{
                if(hit.gameObject.GetComponent<StructureObject>().WhatIsMyStructureObjectTag == StructureObjectTag.Floor)
				return;
			}


			if (!collisionsInAction.Contains(hit.gameObject.name))
			{
				collisionsInAction.Add(hit.gameObject.name);
			}

			Rigidbody body = hit.collider.attachedRigidbody;
			// don't move the rigidbody if the character is on top of it
			if (m_CollisionFlags == CollisionFlags.Below)
			{
				return;
			}

			if (body == null || body.isKinematic)
			{
				return;
			}

			//push objects out of the way if moving through them and they are Moveable or CanPickup (Physics)
			if (PushMode)
			{
				float pushPower = 2.0f;
				Vector3 pushDir = new Vector3(hit.moveDirection.x, 0, hit.moveDirection.z);
				body.velocity = pushDir * pushPower;
			}
			//if we touched something with a rigidbody that needs to simulate physics, generate a force at the impact point
			//body.AddForce(m_CharacterController.velocity * 15f, ForceMode.Force);
			//body.AddForceAtPosition (m_CharacterController.velocity * 15f, hit.point, ForceMode.Acceleration);//might have to adjust the force vector scalar later
		}

		protected void snapAgentToGrid()
		{
            if (this.snapToGrid) {
                float mult = 1 / gridSize;
                float gridX = Convert.ToSingle(Math.Round(this.transform.position.x * mult) / mult);
                float gridZ = Convert.ToSingle(Math.Round(this.transform.position.z * mult) / mult);

                this.transform.position = new Vector3(gridX, transform.position.y, gridZ);
            }
		}

		//move in cardinal directions
		virtual protected void moveCharacter(ServerAction action, int targetOrientation)
		{
            // TODO: Simplify this???
			//resetHand(); when I looked at this resetHand in DiscreteRemoteFPSAgent was just commented out doing nothing so...
			Console.WriteLine("moveCharacter reached for objectId " + action.objectId);

			moveMagnitude = gridSize;
			if (action.moveMagnitude > 0)
			{
				moveMagnitude = action.moveMagnitude;
			}
			int currentRotation = (int)Math.Round(transform.rotation.eulerAngles.y, 0);
			Dictionary<int, Vector3> actionOrientation = new Dictionary<int, Vector3>();
			actionOrientation.Add(0, new Vector3(0f, 0f, 1.0f));
			actionOrientation.Add(90, new Vector3(1.0f, 0.0f, 0.0f));
			actionOrientation.Add(180, new Vector3(0f, 0f, -1.0f));
			actionOrientation.Add(270, new Vector3(-1.0f, 0.0f, 0.0f));
			int delta = (currentRotation + targetOrientation) % 360;

			Vector3 m;
			if (actionOrientation.ContainsKey(delta))
			{
				m = actionOrientation[delta];

			}

			else
			{
				actionOrientation = new Dictionary<int, Vector3>();
				actionOrientation.Add(0, transform.forward);
				actionOrientation.Add(90, transform.right);
				actionOrientation.Add(180, transform.forward * -1);
				actionOrientation.Add(270, transform.right * -1);
				m = actionOrientation[targetOrientation];
			}

			m *= moveMagnitude;

			m.y = Physics.gravity.y * this.m_GravityMultiplier;
			m_CharacterController.Move(m);
			actionFinished(true);
			// StartCoroutine(checkMoveAction(action));
		}

        //do not use this base version, use the override from PhysicsRemote or Stochastic
		public virtual void MoveLeft(ServerAction action)
		{
			moveCharacter(action, 270);
		}

		public virtual void MoveRight(ServerAction action)
		{
			moveCharacter(action, 90);
		}

		public virtual void MoveAhead(ServerAction action)
		{
			moveCharacter(action, 0);
		}

		public virtual void MoveBack(ServerAction action)
		{
			moveCharacter(action, 180);
		}

        //overriden by stochastic
    public virtual void MoveRelative(ServerAction action) {
					Console.WriteLine("MoveRelative reached for objectId " + action.objectId);
		      var moveLocal = new Vector3(action.x, 0, action.z);
		      Vector3 moveWorldSpace = transform.rotation * moveLocal;
		      moveWorldSpace.y = Physics.gravity.y * this.m_GravityMultiplier;
					m_CharacterController.Move(moveWorldSpace);
					actionFinished(true);
    }

		//free rotate, change forward facing of Agent
        //this is currently overrided by Rotate in Stochastic Controller
		public virtual void Rotate(ServerAction response)
		{
			transform.rotation = Quaternion.Euler(new Vector3(0.0f, response.rotation.y, 0.0f));
			actionFinished(true);
		}

		//rotates controlCommand.degrees degrees left w/ respect to current forward
		public virtual void RotateLeft(ServerAction controlCommand)
		{
            transform.Rotate(0, -controlCommand.degrees, 0);
			actionFinished(true);
		}

		//rotates controlCommand.degrees degrees right w/ respect to current forward
		public virtual void RotateRight(ServerAction controlCommand)
		{
            transform.Rotate(0, controlCommand.degrees, 0);
			actionFinished(true);
		}

		//iterates to next allowed downward horizon angle for AgentCamera (max 60 degrees down)
		public virtual void LookDown(ServerAction controlCommand)
		{
			m_Camera.transform.Rotate(controlCommand.degrees, 0, 0);
			actionFinished(true);
		}

		//iterates to next allowed upward horizon angle for agent camera (max 30 degrees up)
		public virtual void LookUp(ServerAction controlCommand)
		{
			m_Camera.transform.Rotate(-controlCommand.degrees, 0, 0);
			actionFinished(true);
		}

        protected T[] flatten2DimArray<T>(T[, ] array) {
            int nrow = array.GetLength(0);
            int ncol = array.GetLength(1);
            T[] flat = new T[nrow * ncol];
            for (int i = 0; i < nrow; i++) {
                for (int j = 0; j < ncol; j++) {
                    flat[i * ncol + j] = array[i, j];
                }
            }
            return flat;
        }

        protected T[] flatten3DimArray<T>(T[, , ] array) {
            int n0 = array.GetLength(0);
            int n1 = array.GetLength(1);
            int n2 = array.GetLength(2);
            T[] flat = new T[n0 * n1 * n2];
            for (int i = 0; i < n0; i++) {
                for (int j = 0; j < n1; j++) {
                    for (int k = 0; k < n2; k++) {
                        flat[i * n1 * n2 + j * n2 + k] = array[i, j, k];
                    }
                }
            }
            return flat;
        }

        protected List<Vector3> visibleRange() {
            int n = 5;
            List<Vector3> points = new List<Vector3>();
            points.Add(transform.position);
            updateAllAgentCollidersForVisibilityCheck(false);
            for (int i = 0; i < n; i++) {
                for (int j = 0; j < n; j++) {
                    RaycastHit hit;
                    Ray ray = m_Camera.ViewportPointToRay(new Vector3(
                        (i + 0.5f) / n, (j + 0.5f) / n, 0.0f));
                    if (Physics.Raycast(ray, out hit, 100f, (1 << 8) | (1 << 10))) {
                        points.Add(hit.point);
                    }
                }
            }
            updateAllAgentCollidersForVisibilityCheck(true);
            return points;
        }

        //*** Maybe make this better */
        // This function should be called before and after doing a visibility check (before with
        // enableColliders == false and after with it equaling true). It, in particular, will
        // turn off/on all the colliders on agents which should not block visibility for the current agent
        // (invisible agents for example).
        protected void updateAllAgentCollidersForVisibilityCheck(bool enableColliders)
        {
            foreach (BaseFPSAgentController agent in this.agentManager.agents)
            {
                bool overlapping = (transform.position - agent.transform.position).magnitude < 0.001f;
                if (overlapping || agent == this || !agent.IsVisible)
                {
                    foreach (Collider c in agent.GetComponentsInChildren<Collider>())
                    {
                        if (ItemInHand == null || !hasAncestor(c.transform.gameObject, ItemInHand))
                        {
                            c.enabled = enableColliders;
                        }
                    }
                }
            }
        }

        protected bool hasAncestor(GameObject child, GameObject potentialAncestor) {
            if (child == potentialAncestor) {
                return true;
            } else if (child.transform.parent != null) {
                return hasAncestor(child.transform.parent.gameObject, potentialAncestor);
            } else {
                return false;
            }
        }

        protected bool ancestorHasName(GameObject go, string name) {
            if (go.name == name) {
                return true;
            } else if (go.transform.parent != null) {
                return ancestorHasName(go.transform.parent.gameObject, name);
            } else {
                return false;
            }
        }

        protected static SimObjPhysics ancestorSimObjPhysics(GameObject go) {
            if (go == null) {
                return null;
            }
            SimObjPhysics so = go.GetComponent<SimObjPhysics>();
            if (so != null) {
                return so;
            } else if (go.transform.parent != null) {
                return ancestorSimObjPhysics(go.transform.parent.gameObject);
            } else {
                return null;
            }
        }

        public void VisibleRange(ServerAction action) {
            actionFinished(true, visibleRange());
        }

        public float TimeSinceStart() {
            return Time.time;
        }

        public SimObjPhysics[] VisibleSimObjs(ServerAction action)
        {
            List<SimObjPhysics> simObjs = new List<SimObjPhysics>();

            //go through array of sim objects visible to the camera
            foreach (SimObjPhysics so in VisibleSimObjs(action.forceVisible))
            {

                if (!string.IsNullOrEmpty(action.objectId) && action.objectId != so.ObjectID)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(action.objectType) && action.GetSimObjType() != so.Type)
                {
                    continue;
                }

                simObjs.Add(so);
            }

            return simObjs.ToArray();
        }

        //pass in forceVisible bool to force grab all objects of type sim obj
        //if not, gather all visible sim objects maxVisibleDistance away from camera view
        public SimObjPhysics[] VisibleSimObjs(bool forceVisible)
        {
            if (forceVisible)
            {
                return GameObject.FindObjectsOfType(typeof(SimObjPhysics)) as SimObjPhysics[];
            }

            else
            {
                return GetAllVisibleSimObjPhysics(m_Camera, maxVisibleDistance);
            }
        }

        protected SimObjPhysics[] GetAllVisibleSimObjPhysics(Camera agentCamera, float maxDistance)
        {
            #if UNITY_EDITOR
            foreach (KeyValuePair<string, SimObjPhysics> pair in physicsSceneManager.ObjectIdToSimObjPhysics)
            {
                // Set all objects to not be visible
                pair.Value.isVisible = false;
            }
            #endif

            List<SimObjPhysics> currentlyVisibleItems = new List<SimObjPhysics>();

            Vector3 agentCameraPos = agentCamera.transform.position;

            //get all sim objects in range around us that have colliders in layer 8 (visible), ignoring objects in the SimObjInvisible layer
            //this will make it so the receptacle trigger boxes don't occlude the objects within them.
            CapsuleCollider agentCapsuleCollider = GetComponent<CapsuleCollider>();
            Vector3 point0, point1;
            float radius;
            agentCapsuleCollider.ToWorldSpaceCapsule(out point0, out point1, out radius);
            if (point0.y <= point1.y)
            {
                point1.y += maxDistance;
            }

            else
            {
                point0.y += maxDistance;
            }

            // Turn off the colliders corresponding to this agent
            // and any invisible agents.
            updateAllAgentCollidersForVisibilityCheck(false);

            Collider[] colliders_in_view = Physics.OverlapCapsule(point0, point1, maxDistance, 1 << 8, QueryTriggerInteraction.Collide);

            if (colliders_in_view != null)
            {
                HashSet<SimObjPhysics> testedSops = new HashSet<SimObjPhysics>();
                foreach (Collider item in colliders_in_view)
                {
                    SimObjPhysics sop = ancestorSimObjPhysics(item.gameObject);
                    //now we have a reference to our sim object
                    if (sop != null && !testedSops.Contains(sop))
                    {
                        testedSops.Add(sop);
                        //check against all visibility points, accumulate count. If at least one point is visible, set object to visible
                        if (sop.VisibilityPoints == null || sop.VisibilityPoints.Length > 0)
                        {
                            Transform[] visPoints = sop.VisibilityPoints;
                            int visPointCount = 0;

                            foreach (Transform point in visPoints)
                            {
                                //if this particular point is in view...
                                if (CheckIfVisibilityPointInViewport(sop, point, agentCamera, false))
                                {
                                    visPointCount++;
                                    #if !UNITY_EDITOR
                                    // If we're in the unity editor then don't break on finding a visible
                                    // point as we want to draw lines to each visible point.
                                    break;
                                    #endif
                                }
                            }

                            //if we see at least one vis point, the object is "visible"
                            if (visPointCount > 0)
                            {
                                #if UNITY_EDITOR
                                sop.isVisible = true;
                                #endif
                                if (!currentlyVisibleItems.Contains(sop))
                                {
                                    currentlyVisibleItems.Add(sop);
                                }
                            }
                        }

                        else
                        {
                            Debug.Log("Error! Set at least 1 visibility point on SimObjPhysics " + sop + ".");
                        }
                    }
                }
            }

            //check against anything in the invisible layers that we actually want to have occlude things in this round.
            //normally receptacle trigger boxes must be ignored from the visibility check otherwise objects inside them will be occluded, but
            //this additional check will allow us to see inside of receptacle objects like cabinets/fridges by checking for that interior
            //receptacle trigger box. Oh boy!
            Collider[] invisible_colliders_in_view = Physics.OverlapCapsule(point0, point1, maxDistance, 1 << 9, QueryTriggerInteraction.Collide);

            if (invisible_colliders_in_view != null)
            {
                foreach (Collider item in invisible_colliders_in_view)
                {
                    if (item.tag == "Receptacle")
                    {
                        SimObjPhysics sop;

                        sop = item.GetComponentInParent<SimObjPhysics>();

                        //now we have a reference to our sim object
                        if (sop)
                        {
                            //check against all visibility points, accumulate count. If at least one point is visible, set object to visible
                            if (sop.VisibilityPoints.Length > 0)
                            {
                                Transform[] visPoints = sop.VisibilityPoints;
                                int visPointCount = 0;

                                foreach (Transform point in visPoints)
                                {
                                    //if this particular point is in view...
                                    if (CheckIfVisibilityPointInViewport(sop, point, agentCamera, true))
                                    {
                                        visPointCount++;
                                    }
                                }

                                //if we see at least one vis point, the object is "visible"
                                if (visPointCount > 0)
                                {
                                    #if UNITY_EDITOR
                                    sop.isVisible = true;
                                    #endif
                                    if (!currentlyVisibleItems.Contains(sop))
                                    {
                                        currentlyVisibleItems.Add(sop);
                                    }
                                }
                            }

                            else
                                Debug.Log("Error! Set at least 1 visibility point on SimObjPhysics prefab!");
                        }
                    }
                }
            }

            // Turn back on the colliders corresponding to this agent and invisible agents.
            updateAllAgentCollidersForVisibilityCheck(true);

            //populate array of visible items in order by distance
            currentlyVisibleItems.Sort((x, y) => Vector3.Distance(x.transform.position, agentCameraPos).CompareTo(Vector3.Distance(y.transform.position, agentCameraPos)));
            return currentlyVisibleItems.ToArray();
        }

        //check if the visibility point on a sim object, sop, is within the viewport
        //has a inclueInvisible bool to check against triggerboxes as well, to check for visibility with things like Cabinets/Drawers
        protected bool CheckIfVisibilityPointInViewport(SimObjPhysics sop, Transform point, Camera agentCamera, bool includeInvisible)
        {
            bool result = false;

            Vector3 viewPoint = agentCamera.WorldToViewportPoint(point.position);

            float ViewPointRangeHigh = 1.0f;
            float ViewPointRangeLow = 0.0f;

            if (viewPoint.z > 0 //&& viewPoint.z < maxDistance * DownwardViewDistance //is in front of camera and within range of visibility sphere
                &&
                viewPoint.x < ViewPointRangeHigh && viewPoint.x > ViewPointRangeLow //within x bounds of viewport
                &&
                viewPoint.y < ViewPointRangeHigh && viewPoint.y > ViewPointRangeLow) //within y bounds of viewport
            {
                //now cast a ray out toward the point, if anything occludes this point, that point is not visible
                RaycastHit hit;

                float distFromPointToCamera = Vector3.Distance(point.position, m_Camera.transform.position);

                //adding slight buffer to this distance to ensure the ray goes all the way to the collider of the object being cast to
                float raycastDistance = distFromPointToCamera + 0.5f;

                LayerMask mask = (1 << 8) | (1 << 9) | (1 << 10);

                //change mask if its a floor so it ignores the receptacle trigger boxes on the floor
                if(sop.Type == SimObjType.Floor)
                mask = (1 << 8) | (1 << 10);


                //check raycast against both visible and invisible layers, to check against ReceptacleTriggerBoxes which are normally
                //ignored by the other raycast
                if (includeInvisible)
                {
                    if (Physics.Raycast(agentCamera.transform.position, point.position - agentCamera.transform.position, out hit, raycastDistance, mask))
                    {
                        if (hit.transform != sop.transform)
                        {
                            result = false;
                        }

                        //if this line is drawn, then this visibility point is in camera frame and not occluded
                        //might want to use this for a targeting check as well at some point....
                        else
                        {
                            result = true;
                            sop.isInteractable = true;

                            #if UNITY_EDITOR
                            Debug.DrawLine(agentCamera.transform.position, point.position, Color.cyan);
                            #endif
                        }
                    }
                }

                //only check against the visible layer, ignore the invisible layer
                //so if an object ONLY has colliders on it that are not on layer 8, this raycast will go through them
                else
                {
                    if (Physics.Raycast(agentCamera.transform.position, point.position - agentCamera.transform.position, out hit, raycastDistance, (1 << 8) | (1 << 10)))
                    {
                        if (hit.transform != sop.transform)
                        {
                            //we didn't directly hit the sop we are checking for with this cast,
                            //check if it's because we hit something see-through
                            SimObjPhysics hitSop = hit.transform.GetComponent<SimObjPhysics>();
                            if (hitSop != null && hitSop.DoesThisObjectHaveThisSecondaryProperty(SimObjSecondaryProperty.CanSeeThrough))
                            {
                                //we hit something see through, so now find all objects in the path between
                                //the sop and the camera
                                RaycastHit[] hits;
                                hits = Physics.RaycastAll(agentCamera.transform.position, point.position - agentCamera.transform.position,
                                    raycastDistance, (1 << 8), QueryTriggerInteraction.Ignore);

                                float[] hitDistances = new float[hits.Length];
                                for (int i = 0; i < hitDistances.Length; i++)
                                {
                                    hitDistances[i] = hits[i].distance; //Vector3.Distance(hits[i].transform.position, m_Camera.transform.position);
                                }

                                Array.Sort(hitDistances, hits);

                                foreach (RaycastHit h in hits)
                                {

                                    if (h.transform == sop.transform)
                                    {
                                        //found the object we are looking for, great!
                                        result = true;
                                        break;
                                    }

                                    else
                                    {
                                        // Didn't find it, continue on only if the hit object was translucent
                                        SimObjPhysics sopHitOnPath = null;
                                        sopHitOnPath = h.transform.GetComponentInParent<SimObjPhysics>();
                                        if (sopHitOnPath == null || !sopHitOnPath.DoesThisObjectHaveThisSecondaryProperty(SimObjSecondaryProperty.CanSeeThrough))
                                        {
                                            break;
                                        }
                                    }
                                }
                            }
                        }

                        else
                        {
                            //if this line is drawn, then this visibility point is in camera frame and not occluded
                            //might want to use this for a targeting check as well at some point....
                            result = true;
                            sop.isInteractable = true;
                        }
                    }
                }
            }

            #if UNITY_EDITOR
            if (result == true)
            {
                Debug.DrawLine(agentCamera.transform.position, point.position, Color.cyan);
            }
            #endif

            return result;
        }

        public void DefaultAgentHand(ServerAction action = null) {
            ResetAgentHandPosition(action);
            ResetAgentHandRotation(action);
            //SetUpRotationBoxChecks();
            IsHandDefault = true;
        }

        public void ResetAgentHandPosition(ServerAction action = null) {
            AgentHand.transform.position = DefaultHandPosition.transform.position;
        }

        public void ResetAgentHandRotation(ServerAction action = null) {
            AgentHand.transform.localRotation = Quaternion.Euler(Vector3.zero);
        }

        //randomly repositions sim objects in the current scene
        public void InitialRandomSpawn(ServerAction action)
        {
            //something is in our hand AND we are trying to spawn it. Quick drop the object
            if (ItemInHand != null)
            {
                Rigidbody rb = ItemInHand.GetComponent<Rigidbody>();
                rb.isKinematic = false;
                rb.constraints = RigidbodyConstraints.None;
                rb.useGravity = true;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

                GameObject topObject = GameObject.Find("Objects");
                if (topObject != null)
                {
                    ItemInHand.transform.parent = topObject.transform;
                }

                else
                {
                    ItemInHand.transform.parent = null;
                }

                rb.angularVelocity = UnityEngine.Random.insideUnitSphere;

                ItemInHand.GetComponent<SimObjPhysics>().isInAgentHand = false;//agent hand flag
                DefaultAgentHand();//also default agent hand
                ItemInHand = null;
            }

            //default number of attempts if no value is passed in.
            if (action.numPlacementAttempts == 0)
            {
                action.numPlacementAttempts = 5;
            }

            //default excludedReceptacles if null
            if (action.excludedReceptacles == null)
            {
                action.excludedReceptacles = new String[0];
            }

            List<SimObjType> listOfExcludedReceptacles = new List<SimObjType>();

            //check if strings used for excludedReceptacles are valid object types
            foreach (string receptacleType in action.excludedReceptacles)
            {
                try
                {
                    SimObjType objType = (SimObjType)System.Enum.Parse(typeof(SimObjType), receptacleType);
                    listOfExcludedReceptacles.Add(objType);
                }

                catch (Exception)
                {
                    errorMessage = "invalid Object Type used in excludedReceptacles array: " + receptacleType;
                    actionFinished(false);
                    return;
                }
            }

            bool success = physicsSceneManager.RandomSpawnRequiredSceneObjects(
                action.randomSeed,
                action.forceVisible,
                action.numPlacementAttempts,
                action.placeStationary,
                action.numDuplicatesOfType,
                listOfExcludedReceptacles
                );
            physicsSceneManager.ResetObjectIdToSimObjPhysics();
            actionFinished(success);
        }

        // On demand public function for getting what sim objects are visible at that moment
        public List<SimObjPhysics> GetAllVisibleSimObjPhysics(float maxDistance) {
            List<SimObjPhysics> currentlyVisibleItems = new List<SimObjPhysics>();
            CapsuleCollider agentCapsuleCollider = this.GetComponent<CapsuleCollider>();
            var camera = this.GetComponentInChildren<Camera>();
            Vector3 point0, point1;
            float radius;

            agentCapsuleCollider.ToWorldSpaceCapsule(out point0, out point1, out radius);
            if (point0.y <= point1.y) {
                point1.y += maxDistance;
            } else {
                point0.y += maxDistance;
            }

            this.updateAllAgentCollidersForVisibilityCheck(false);
            Collider[] colliders_in_view = Physics.OverlapCapsule(point0, point1, maxDistance, 1 << 8, QueryTriggerInteraction.Collide);

            if (colliders_in_view != null) {
                HashSet<SimObjPhysics> testedSops = new HashSet<SimObjPhysics>();
                foreach (Collider item in colliders_in_view) {
                    SimObjPhysics sop = ancestorSimObjPhysics(item.gameObject);

                    //now we have a reference to our sim object
                    if (sop != null && !testedSops.Contains(sop)) {
                        testedSops.Add(sop);
                        //check against all visibility points, accumulate count. If at least one point is visible, set object to visible
                        if (sop.VisibilityPoints == null || sop.VisibilityPoints.Length > 0) {
                            Transform[] visPoints = sop.VisibilityPoints;
                            int visPointCount = 0;

                            foreach (Transform point in visPoints) {



                                //if this particular point is in view...
                                if (CheckIfVisibilityPointInViewport(sop, point, camera, false)) {
                                    visPointCount++;

                                    #if !UNITY_EDITOR
                                    // If we're in the unity editor then don't break on finding a visible
                                    // point as we want to draw lines to each visible point.
                                    break;
                                    #endif
                                }
                            }

                            //if we see at least one vis point, the object is "visible"
                            if (visPointCount > 0) {
                                //  Debug.Log("------ Visible " + sop.Type);
                                #if UNITY_EDITOR
                                sop.isVisible = true;
                                #endif
                                if (!currentlyVisibleItems.Contains(sop)) {
                                    currentlyVisibleItems.Add(sop);
                                }
                            }
                        } else {
                            Debug.Log("Error! Set at least 1 visibility point on SimObjPhysics " + sop + ".");
                        }

                    }
                }
            }

            this.updateAllAgentCollidersForVisibilityCheck(true);

            return currentlyVisibleItems;
        }

        //not sure what this does, maybe delete?
        public void SetTopLevelView(ServerAction action) {
            inTopLevelView = action.topView;
            actionFinished(true);
        }

        public void ToggleMapView(ServerAction action) {

            SyncTransform[] syncInChildren;

            List<StructureObject> structureObjsList = new List<StructureObject>();
            StructureObject[] structureObjs = FindObjectsOfType(typeof(StructureObject)) as StructureObject[];

            foreach(StructureObject so in structureObjs)
            {
                if(so.WhatIsMyStructureObjectTag == StructureObjectTag.Ceiling)
                {
                    structureObjsList.Add(so);
                }
            }

            if (inTopLevelView) {
                inTopLevelView = false;
                m_Camera.orthographic = false;
                m_Camera.transform.localPosition = lastLocalCameraPosition;
                m_Camera.transform.localRotation = lastLocalCameraRotation;

                //restore agent body culling
                m_Camera.transform.GetComponent<FirstPersonCharacterCull>().StopCullingThingsForASecond = false;
                syncInChildren = gameObject.GetComponentsInChildren<SyncTransform>();
                foreach (SyncTransform sync in syncInChildren)
                {
                    sync.StopSyncingForASecond = false;
                }

                foreach(StructureObject so in structureObjsList)
                {
                    UpdateDisplayGameObject(so.gameObject, true);
                }
            }

            else {

                //stop culling the agent's body so it's visible from the top?
                m_Camera.transform.GetComponent<FirstPersonCharacterCull>().StopCullingThingsForASecond = true;
                syncInChildren = gameObject.GetComponentsInChildren<SyncTransform>();
                foreach (SyncTransform sync in syncInChildren)
                {
                    sync.StopSyncingForASecond = true;
                }

                inTopLevelView = true;
                lastLocalCameraPosition = m_Camera.transform.localPosition;
                lastLocalCameraRotation = m_Camera.transform.localRotation;

                Bounds b = sceneBounds;
                float midX = (b.max.x + b.min.x) / 2.0f;
                float midZ = (b.max.z + b.min.z) / 2.0f;
                m_Camera.transform.rotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
                m_Camera.transform.position = new Vector3(midX, b.max.y + 5, midZ);
                m_Camera.orthographic = true;

                m_Camera.orthographicSize = Math.Max((b.max.x - b.min.x) / 2f, (b.max.z - b.min.z) / 2f);

                cameraOrthSize = m_Camera.orthographicSize;
                foreach(StructureObject so in structureObjsList)
                {
                    UpdateDisplayGameObject(so.gameObject, false);
                }            }
            actionFinished(true);
        }


        public void UpdateDisplayGameObject(GameObject go, bool display) {
            if (go != null) {
                foreach (MeshRenderer mr in go.GetComponentsInChildren<MeshRenderer>() as MeshRenderer[]) {
                    if (!initiallyDisabledRenderers.Contains(mr.GetInstanceID())) {
                        mr.enabled = display;
                    }
                }
            }
        }
         public void VisualizePath(ServerAction action) {
            var path = action.positions;
            if (path == null || path.Count == 0) {
                this.errorMessage = "Invalid path with 0 points.";
                actionFinished(false);
                return;
            }

            var id = action.objectId;

            getReachablePositions(1.0f, 10000, action.grid);

            Instantiate(DebugTargetPointPrefab, path[path.Count-1], Quaternion.identity);
            new List<bool>();
            var go = Instantiate(DebugPointPrefab, path[0], Quaternion.identity);
            var textMesh = go.GetComponentInChildren<TextMesh>();
            textMesh.text = id;

            var lineRenderer = go.GetComponentInChildren<LineRenderer>();
            lineRenderer.startWidth = 0.015f;
            lineRenderer.endWidth = 0.015f;

            lineRenderer.positionCount = path.Count;
            lineRenderer.SetPositions(path.ToArray());
            actionFinished(true);
        }

        //this one is used for in-editor debug draw, currently calls to this are commented out
        private void VisualizePath(Vector3 startPosition, NavMeshPath path) {
            var pathDistance = 0.0;

            for (int i = 0; i < path.corners.Length - 1; i++) {
                Debug.DrawLine(path.corners[i], path.corners[i + 1], Color.red, 10.0f);
                Debug.Log("P i:" + i + " : " + path.corners[i] + " i+1:" + i + 1 + " : " + path.corners[i]);
                pathDistance += Vector3.Distance(path.corners[i], path.corners[i + 1]);
            }

            if (pathDistance > 0.0001 ) {
                // Better way to draw spheres
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
                go.GetComponent<Collider>().enabled = false;
                go.transform.position = startPosition;
            }
        }

        private string[] objectTypeToObjectIds(string objectTypeString) {
            List<string> objectIds = new List<string>();
            try {
                SimObjType objectType = (SimObjType) Enum.Parse(typeof(SimObjType), objectTypeString.Replace(" ", String.Empty), true);
                foreach (var s in physicsSceneManager.ObjectIdToSimObjPhysics) {
                    if (s.Value.ObjType == objectType) {
                        objectIds.Add(s.Value.objectID);
                    }
                }
            }
            catch (ArgumentException exception) {
                Debug.Log(exception);
            }
            return objectIds.ToArray();
        }

        public void ObjectTypeToObjectIds(ServerAction action) {
            try {
                var objectIds = objectTypeToObjectIds(action.objectType);
                actionFinished(true, objectIds.ToArray());
            }
            catch (ArgumentException exception) {
                errorMessage = "Invalid object type '" + action.objectType + "'. " + exception.Message;
                actionFinished(false);
            }
        }

        private SimObjPhysics getSimObjectFromTypeOrId(ServerAction action) {
            var objectId = action.objectId;
            if (!String.IsNullOrEmpty(action.objectType) && String.IsNullOrEmpty(action.objectId)) {
                var ids = objectTypeToObjectIds(action.objectType);
                if (ids.Length == 0) {
                    errorMessage = "Object type '" + action.objectType + "' was not found in the scene.";
                    return null;
                }
                else if (ids.Length > 1) {
                    errorMessage = "Multiple objects of type '" + action.objectType + "' were found in the scene, cannot disambiguate.";
                    return null;
                }

                objectId = ids[0];
            }

            if (!physicsSceneManager.ObjectIdToSimObjPhysics.ContainsKey(objectId)) {
                errorMessage = "Cannot find sim object with id '" + objectId + "'";
                return null;
            }

            SimObjPhysics sop = physicsSceneManager.ObjectIdToSimObjPhysics[objectId];
            if (sop == null) {
                errorMessage = "Object with id '" + objectId+ "' is null";
                return null;
            }

            return sop;
        }

        public void VisualizeGrid(ServerAction action) {
            var reachablePositions = getReachablePositions(1.0f, 10000, true);
            actionFinished(true, reachablePositions);
        }

        public void GetShortestPath(ServerAction action) {
            SimObjPhysics sop = getSimObjectFromTypeOrId(action);
            if (sop == null) {
                actionFinished(false);
                return;
            }
            var startPosition = this.transform.position;
            var startRotation = this.transform.rotation;
            if (!action.useAgentTransform) {
                startPosition = action.position;
                startRotation = Quaternion.Euler(action.rotation);
            }

            var path = GetSimObjectNavMeshTarget(sop, startPosition, startRotation);
            if (path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete) {
                //VisualizePath(startPosition, path);
                actionFinished(true, path);
                return;
            }
            else {
                errorMessage = "Path to target could not be found";
                actionFinished(false);
                return;
            }
        }

        private bool GetPathFromReachablePositions(
            IEnumerable<Vector3> sortedPositions,
            Vector3 targetPosition,
            Transform agentTransform,
            string targetSimObjectId,
            UnityEngine.AI.NavMeshPath path) {

            Vector3 fixedPosition = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            //bool success = false;
            var PhysicsController = this;
            foreach (var pos in sortedPositions) {
                agentTransform.position = pos;
                agentTransform.LookAt(targetPosition);

                var visibleSimObjects = PhysicsController.GetAllVisibleSimObjPhysics(PhysicsController.maxVisibleDistance);
                if (visibleSimObjects.Any(sop => sop.objectID == targetSimObjectId)) {
                    fixedPosition = pos;
                    //success = true;
                    break;
                }
            }

            var pathSuccess =  UnityEngine.AI.NavMesh.CalculatePath(agentTransform.position, fixedPosition,  UnityEngine.AI.NavMesh.AllAreas, path);
            return pathSuccess;
        }

        protected Collider[] overlapCollider(BoxCollider box, Vector3 newCenter, float rotateBy, int layerMask) {
            Vector3 center, halfExtents;
            Quaternion orientation;
            box.ToWorldSpaceBox(out center, out halfExtents, out orientation);
            orientation = Quaternion.Euler(0f, rotateBy, 0f) * orientation;

            return Physics.OverlapBox(newCenter, halfExtents, orientation, layerMask, QueryTriggerInteraction.Ignore);
        }

        protected Collider[] overlapCollider(SphereCollider sphere, Vector3 newCenter, int layerMask) {
            Vector3 center;
            float radius;
            sphere.ToWorldSpaceSphere(out center, out radius);
            return Physics.OverlapSphere(newCenter, radius, layerMask, QueryTriggerInteraction.Ignore);
        }

        protected Collider[] overlapCollider(CapsuleCollider capsule, Vector3 newCenter, float rotateBy, int layerMask) {
            Vector3 point0, point1;
            float radius;
            capsule.ToWorldSpaceCapsule(out point0, out point1, out radius);

            // Normalizing
            Vector3 oldCenter = (point0 + point1) / 2.0f;
            point0 = point0 - oldCenter;
            point1 = point1 - oldCenter;

            // Rotating and recentering
            var rotator = Quaternion.Euler(0f, rotateBy, 0f);
            point0 = rotator * point0 + newCenter;
            point1 = rotator * point1 + newCenter;

            return Physics.OverlapCapsule(point0, point1, radius, layerMask, QueryTriggerInteraction.Ignore);
        }

        protected bool handObjectCanFitInPosition(Vector3 newAgentPosition, float rotation) {
            if (ItemInHand == null) {
                return true;
            }

            SimObjPhysics soInHand = ItemInHand.GetComponent<SimObjPhysics>();

            Vector3 handObjPosRelAgent =
                Quaternion.Euler(0, rotation - transform.rotation.y, 0) *
                (transform.position - ItemInHand.transform.position);

            Vector3 newHandPosition = handObjPosRelAgent + newAgentPosition;

            int layerMask = 1 << 8;
            foreach (CapsuleCollider cc in soInHand.GetComponentsInChildren<CapsuleCollider>()) {
                foreach (Collider c in overlapCollider(cc, newHandPosition, rotation, layerMask)) {
                    if (!hasAncestor(c.transform.gameObject, gameObject)) {
                        return false;
                    }
                }
            }

            foreach (BoxCollider bc in soInHand.GetComponentsInChildren<BoxCollider>()) {
                foreach (Collider c in overlapCollider(bc, newHandPosition, rotation, layerMask)) {
                    if (!hasAncestor(c.transform.gameObject, gameObject)) {
                        return false;
                    }
                }
            }

            foreach (SphereCollider sc in soInHand.GetComponentsInChildren<SphereCollider>()) {
                foreach (Collider c in overlapCollider(sc, newHandPosition, layerMask)) {
                    if (!hasAncestor(c.transform.gameObject, gameObject)) {
                        return false;
                    }
                }
            }

            return true;
        }

        //cast a capsule the same size as the agent
        //used to check for collisions
        public RaycastHit[] capsuleCastAllForAgent(
            CapsuleCollider cc,
            float skinWidth,
            Vector3 startPosition,
            Vector3 dir,
            float moveMagnitude,
            int layerMask
            ) {
            Vector3 center = cc.transform.position + cc.center;//make sure to offset this by cc.center since we shrank the capsule size
            float radius = cc.radius + skinWidth;
            float innerHeight = cc.height / 2.0f - radius;
            Vector3 point1 = new Vector3(startPosition.x, center.y + innerHeight, startPosition.z);
            Vector3 point2 = new Vector3(startPosition.x, center.y - innerHeight + skinWidth, startPosition.z);
            return Physics.CapsuleCastAll(
                point1,
                point2,
                radius,
                dir,
                moveMagnitude,
                layerMask,
                QueryTriggerInteraction.Ignore
            );
        }

        protected Collider[] objectsCollidingWithAgent() {
            int layerMask = 1 << 8;
            return PhysicsExtensions.OverlapCapsule(GetComponent<CapsuleCollider>(), layerMask, QueryTriggerInteraction.Ignore);
        }

        public bool  getReachablePositionToObjectVisible(SimObjPhysics targetSOP, out Vector3 pos, float gridMultiplier = 1.0f, int maxStepCount = 10000) {
            CapsuleCollider cc = GetComponent<CapsuleCollider>();
            float sw = m_CharacterController.skinWidth;
            Queue<Vector3> pointsQueue = new Queue<Vector3>();
            pointsQueue.Enqueue(transform.position);
            Vector3[] directions = {
                new Vector3(1.0f, 0.0f, 0.0f),
                new Vector3(0.0f, 0.0f, 1.0f),
                new Vector3(-1.0f, 0.0f, 0.0f),
                new Vector3(0.0f, 0.0f, -1.0f)
            };
            Quaternion originalRot = transform.rotation;

            HashSet<Vector3> goodPoints = new HashSet<Vector3>();
            int layerMask = 1 << 8;
            int stepsTaken = 0;
            pos = Vector3.negativeInfinity;
            while (pointsQueue.Count != 0) {
                stepsTaken += 1;
                Vector3 p = pointsQueue.Dequeue();
                if (!goodPoints.Contains(p)) {
                    goodPoints.Add(p);
                    transform.position = p;
                    var rot = transform.rotation;
                    //make sure to rotate just the Camera, not the whole agent
                    m_Camera.transform.LookAt(targetSOP.transform, transform.up);

                    var visibleSimObjects = this.GetAllVisibleSimObjPhysics(this.maxVisibleDistance);
                    transform.rotation = rot;

                    if (visibleSimObjects.Any(sop => sop.objectID == targetSOP.objectID)) {

                        pos = p;
                        return true;
                    }

                    HashSet<Collider> objectsAlreadyColliding = new HashSet<Collider>(objectsCollidingWithAgent());
                    foreach (Vector3 d in directions) {
                        RaycastHit[] hits = capsuleCastAllForAgent(
                            cc,
                            sw,
                            p,
                            d,
                            (gridSize * gridMultiplier),
                            layerMask
                        );

                        bool shouldEnqueue = true;
                        foreach (RaycastHit hit in hits) {
                            if (hit.transform.gameObject.name != "Floor" &&
                                !ancestorHasName(hit.transform.gameObject, "FPSController") &&
                                !objectsAlreadyColliding.Contains(hit.collider)
                            ) {
                                shouldEnqueue = false;
                                break;
                            }
                        }
                        Vector3 newPosition = p + d * gridSize * gridMultiplier;
                        bool inBounds = sceneBounds.Contains(newPosition);
                        if (errorMessage == "" && !inBounds) {
                            errorMessage = "In " +
                                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name +
                                ", position " + newPosition.ToString() +
                                " can be reached via capsule cast but is beyond the scene bounds.";
                        }

                        shouldEnqueue = shouldEnqueue && inBounds && (
                            handObjectCanFitInPosition(newPosition, 0.0f) ||
                            handObjectCanFitInPosition(newPosition, 90.0f) ||
                            handObjectCanFitInPosition(newPosition, 180.0f) ||
                            handObjectCanFitInPosition(newPosition, 270.0f)
                        );
                        if (shouldEnqueue) {
                            pointsQueue.Enqueue(newPosition);
                            #if UNITY_EDITOR
                                Debug.DrawLine(p, newPosition, Color.cyan, 100000f);
                            #endif
                        }
                    }
                }
                if (stepsTaken > Math.Floor(maxStepCount/(gridSize * gridSize))) {
                    errorMessage = "Too many steps taken in GetReachablePositions.";
                    break;
                }
            }

            Vector3[] reachablePos = new Vector3[goodPoints.Count];
            goodPoints.CopyTo(reachablePos);
            #if UNITY_EDITOR
                Debug.Log(reachablePos.Length);
            #endif
            return false;
        }

        private UnityEngine.AI.NavMeshPath GetSimObjectNavMeshTarget(SimObjPhysics targetSOP, Vector3 initialPosition, Quaternion initialRotation, bool visualize = false) {
            var targetTransform = targetSOP.transform;
            var targetSimObject = targetTransform.GetComponentInChildren<SimObjPhysics>();
            var PhysicsController = this;
            var agentTransform = PhysicsController.transform;

            var originalAgentPosition = agentTransform.position;
            var orignalAgentRotation = agentTransform.rotation;

            var fixedPosition = Vector3.negativeInfinity;

            agentTransform.position = initialPosition;
            agentTransform.rotation = initialRotation;
            getReachablePositionToObjectVisible(targetSimObject, out fixedPosition);
            agentTransform.position = originalAgentPosition;
            agentTransform.rotation = orignalAgentRotation;
            var path = new UnityEngine.AI.NavMeshPath();
            var sopPos = targetSOP.transform.position;
            //var target = new Vector3(sopPos.x, initialPosition.y, sopPos.z);

            //make sure navmesh agent is active
            this.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled = true;
            //bool pathSuccess = UnityEngine.AI.NavMesh.CalculatePath(initialPosition, fixedPosition,  UnityEngine.AI.NavMesh.AllAreas, path);

            var pathDistance = 0.0f;
            for (int i = 0; i < path.corners.Length - 1; i++) {
                #if UNITY_EDITOR
                    // Debug.DrawLine(path.corners[i], path.corners[i + 1], Color.red, 10.0f);
                #endif
                pathDistance += Vector3.Distance(path.corners[i], path.corners[i + 1]);
            }


            //disable navmesh agent
            this.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled = false;

            return path;
        }

        public void GetShortestPathToPoint(ServerAction action) {
            var startPosition = this.transform.position;
            if (!action.useAgentTransform) {
                startPosition = action.position;
            }

            //var targetPosition = new Vector3(action.x, action.y, action.z);

            var path = new UnityEngine.AI.NavMeshPath();
            this.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled = true;
            //bool pathSuccess = UnityEngine.AI.NavMesh.CalculatePath(startPosition, targetPosition,  UnityEngine.AI.NavMesh.AllAreas, path);
            if (path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete) {
                //VisualizePath(startPosition, path);
                this.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled = false;
                actionFinished(true, path);
                return;
            }
            else {
                errorMessage = "Path to target could not be found";
                this.GetComponent<UnityEngine.AI.NavMeshAgent>().enabled = false;
                actionFinished(false);
                return;
            }
        }
        public void VisualizeShortestPaths(ServerAction action) {

            SimObjPhysics sop = getSimObjectFromTypeOrId(action);
            if (sop == null) {
                actionFinished(false);
                return;
            }

            getReachablePositions(1.0f, 10000, action.grid, action.gridColor);

            Instantiate(DebugTargetPointPrefab, sop.transform.position, Quaternion.identity);
            var results = new List<bool>();
            for (var i = 0; i < action.positions.Count; i++) {
                var pos = action.positions[i];
                var go = Instantiate(DebugPointPrefab, pos, Quaternion.identity);
                var textMesh = go.GetComponentInChildren<TextMesh>();
                textMesh.text = i.ToString();

                var path = GetSimObjectNavMeshTarget(sop, pos, Quaternion.identity);

                var lineRenderer = go.GetComponentInChildren<LineRenderer>();

                if (action.pathGradient != null && action.pathGradient.colorKeys.Length > 0){
                    lineRenderer.colorGradient = action.pathGradient;
                }
                lineRenderer.startWidth = 0.015f;
                lineRenderer.endWidth = 0.015f;

                results.Add(path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete);

                if (path.status == UnityEngine.AI.NavMeshPathStatus.PathComplete) {
                    lineRenderer.positionCount = path.corners.Length;
                    lineRenderer.SetPositions(path.corners.Select(c => new Vector3(c.x, gridVisualizeY + 0.005f, c.z)).ToArray());
                }
            }
            actionFinished(true, results.ToArray());
        }

        public void CameraCrack(ServerAction action)
        {
            GameObject canvas = Instantiate(CrackedCameraCanvas);
            CrackedCameraManager camMan = canvas.GetComponent<CrackedCameraManager>();

            camMan.SpawnCrack(action.randomSeed);
            actionFinished(true);
        }

        public void OnTriggerStay(Collider other)
        {
            if(other.CompareTag("HighFriction"))
            {
                inHighFrictionArea = true;
            }

            else
            {
                inHighFrictionArea = false;
            }
        }

        #if UNITY_EDITOR
        void OnDrawGizmos()
        {
            ////check for valid spawn points in GetSpawnCoordinatesAboveObject action
            // Gizmos.color = Color.magenta;
            // if(validpointlist.Count > 0)
            // {
            //     foreach(Vector3 yes in validpointlist)
            //     {
            //         Gizmos.DrawCube(yes, new Vector3(0.01f, 0.01f, 0.01f));
            //     }
            // }

            //draw axis aligned bounds of objects after actionFinished() calls
            if(gizmobounds != null)
            {
                Gizmos.color = Color.yellow;
                foreach(Bounds g in gizmobounds)
                {
                    Gizmos.DrawWireCube(g.center, g.size);
                }
            }
        }
        #endif
	}
}
