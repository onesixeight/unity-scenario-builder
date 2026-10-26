using UnityEngine;

namespace ScenarioBuilder.Runtime
{
    public sealed class ContextState : MonoBehaviour
    {
        [Tooltip("Уникальное имя состояния для контекстного условия.")]
        public string Key;
        public bool InitialValue;
        [SerializeField] private bool value;

        public bool Value => value;
        public void SetValue(bool newValue) { value = newValue; }
        public void ResetState() { value = InitialValue; }
        private void Awake() { ResetState(); }
    }
}
