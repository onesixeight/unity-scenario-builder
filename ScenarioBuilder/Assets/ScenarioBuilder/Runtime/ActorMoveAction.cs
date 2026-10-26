using UnityEngine;

namespace ScenarioBuilder.Runtime
{
    [AddComponentMenu("Учебные сценарии/Демонстрация/Переместить участника")]
    public sealed class ActorMoveAction : MonoBehaviour
    {
        public DemoActor Actor;
        public Transform Target;

        public void Execute()
        {
            if (isActiveAndEnabled && Actor != null && Target != null)
                Actor.MoveTo(Target);
        }
    }
}
