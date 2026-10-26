using ScenarioBuilder.Authoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace ScenarioBuilder.Editor
{
    [CustomEditor(typeof(ModeDefinition))]
    public sealed class ModeDefinitionEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.Add(new HelpBox(
                "Состав группы и пакеты активации настраиваются отдельно. " +
                "Один шаг в пакете — последовательность; несколько — одновременная активация. " +
                "Порядок меняется перетаскиванием элементов списков.", HelpBoxMessageType.Info));

            InspectorElement.FillDefaultInspector(root, serializedObject, this);
            var result = new HelpBox("", HelpBoxMessageType.Info);
            root.Add(new Button(() => Validate(result)) { text = "Проверить конфигурацию" });
            root.Add(result);
            root.RegisterCallback<SerializedPropertyChangeEvent>(_ => Validate(result));
            Validate(result);
            return root;
        }

        private void Validate(HelpBox result)
        {
            serializedObject.ApplyModifiedProperties();
            var errors = ((ModeDefinition)target).ValidateConfiguration();
            result.messageType = errors.Count == 0 ? HelpBoxMessageType.Info : HelpBoxMessageType.Error;
            result.text = errors.Count == 0
                ? "Конфигурация корректна. Условия создаются заново для каждого прохождения."
                : string.Join("\n", errors);
        }
    }
}
