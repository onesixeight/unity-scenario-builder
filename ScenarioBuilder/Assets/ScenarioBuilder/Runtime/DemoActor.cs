using UnityEngine;

namespace ScenarioBuilder.Runtime
{
    [AddComponentMenu("Учебные сценарии/Демонстрация/Участник")]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class DemoActor : MonoBehaviour
    {
        [InspectorName("Исполнитель")]
        public ScenarioRunner Runner;

        [InspectorName("Скорость"), Min(0.1f), Tooltip("Скорость движения по плоскости XZ, в метрах в секунду.")]
        public float Speed = 8f;

        [InspectorName("Исходная точка"), Tooltip("Куда вернуть участника при новом прохождении. Высота центра тела — 0.5.")]
        public Transform Home;

        [InspectorName("Коридор движения Z"), Tooltip("Перед переходом к другой станции участник отходит на эту координату Z, проходит вдоль безопасного коридора и только затем подходит к цели.")]
        public float CorridorZ = -2f;

        public bool IsMoving => destination != null && waypointIndex < waypointCount;

        private const float ActorHeight = 0.5f;
        private Rigidbody body;
        private Transform destination;
        private readonly Vector3[] waypoints = new Vector3[3];
        private int waypointIndex;
        private int waypointCount;
        private ScenarioRunner subscribedRunner;
        private Vector3 initialPosition;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            initialPosition = OnPlane(body.position);
            body.isKinematic = false;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.position = initialPosition;
        }

        private void OnEnable()
        {
            UnsubscribeRestart();
            subscribedRunner = Runner;
            if (subscribedRunner != null)
                subscribedRunner.Restarted += ResetActor;
        }

        private void OnDisable()
        {
            UnsubscribeRestart();
            StopMoving();
        }

        public void MoveTo(Transform target)
        {
            if (!isActiveAndEnabled || target == null || body == null)
                return;
            StopMoving();
            var targetPosition = OnPlane(target.position);
            if ((targetPosition - body.position).sqrMagnitude < 0.0001f)
                return;

            destination = target;
            AddWaypoint(new Vector3(body.position.x, ActorHeight, CorridorZ));
            AddWaypoint(new Vector3(targetPosition.x, ActorHeight, CorridorZ));
            AddWaypoint(targetPosition);
            body.WakeUp();
        }

        private void FixedUpdate()
        {
            if (destination == null)
                return;

            var targetPosition = waypoints[waypointIndex];
            var nextPosition = Vector3.MoveTowards(body.position, targetPosition,
                Mathf.Max(0.1f, Speed) * Time.fixedDeltaTime);
            body.MovePosition(nextPosition);
            if ((nextPosition - targetPosition).sqrMagnitude < 0.0001f)
            {
                waypointIndex++;
                if (waypointIndex == waypointCount)
                    StopMoving();
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (destination == null || waypointIndex != waypointCount - 1)
                return;
            var hit = collision.collider.transform;
            if (hit == destination || hit.IsChildOf(destination))
                StopMoving();
        }

        public void ResetActor()
        {
            if (body == null)
                body = GetComponent<Rigidbody>();
            StopMoving();
            body.position = Home != null ? OnPlane(Home.position) : initialPosition;
            body.rotation = Quaternion.identity;
            body.WakeUp();
        }

        private void StopMoving()
        {
            destination = null;
            waypointIndex = 0;
            waypointCount = 0;
            if (body == null)
                return;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        private void AddWaypoint(Vector3 point)
        {
            var previous = waypointCount == 0 ? body.position : waypoints[waypointCount - 1];
            if ((point - previous).sqrMagnitude >= 0.0001f)
                waypoints[waypointCount++] = point;
        }

        private void UnsubscribeRestart()
        {
            if (subscribedRunner != null)
                subscribedRunner.Restarted -= ResetActor;
            subscribedRunner = null;
        }

        private static Vector3 OnPlane(Vector3 position)
        {
            position.y = ActorHeight;
            return position;
        }
    }
}
