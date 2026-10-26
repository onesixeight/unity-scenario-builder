using System;
using System.Collections.Generic;

namespace ScenarioBuilder.Core
{
    public sealed class SignalCondition : IStepCondition
    {
        private readonly string successSignal;
        private readonly HashSet<string> failureSignals;

        public SignalCondition(string successSignal, params string[] failureSignals)
        {
            this.successSignal = ConditionSignals.Required(successSignal, nameof(successSignal));
            this.failureSignals = ConditionSignals.Failures(failureSignals, new[] { successSignal });
        }

        public ConditionResult Evaluate(ScenarioAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (action.Signal == successSignal) return new ConditionResult(ConditionOutcome.Success);
            if (failureSignals.Contains(action.Signal ?? string.Empty))
                return new ConditionResult(ConditionOutcome.Violation, ConditionSignals.FailureMessage(action.Signal));
            return new ConditionResult(ConditionOutcome.Ignore);
        }

        public void Reset() { }
    }

    public sealed class OrderedSignalCondition : IStepCondition
    {
        private readonly string[] sequence;
        private readonly HashSet<string> sequenceSignals;
        private readonly HashSet<string> failureSignals;
        private int next;

        public OrderedSignalCondition(string[] sequence, params string[] failureSignals)
        {
            if (sequence == null || sequence.Length == 0)
                throw new ArgumentException("An ordered condition needs at least one signal.", nameof(sequence));
            this.sequence = (string[])sequence.Clone();
            foreach (var signal in this.sequence) ConditionSignals.Required(signal, nameof(sequence));
            sequenceSignals = new HashSet<string>(this.sequence, StringComparer.Ordinal);
            this.failureSignals = ConditionSignals.Failures(failureSignals, this.sequence);
        }

        public ConditionResult Evaluate(ScenarioAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (next >= sequence.Length) return new ConditionResult(ConditionOutcome.Ignore);
            if (action.Signal == sequence[next])
            {
                next++;
                return next == sequence.Length
                    ? new ConditionResult(ConditionOutcome.Success)
                    : new ConditionResult(ConditionOutcome.Progress,
                        "Выполнено " + next + " из " + sequence.Length + ". Далее: " + sequence[next] + ".");
            }
            if (failureSignals.Contains(action.Signal ?? string.Empty))
                return new ConditionResult(ConditionOutcome.Violation, ConditionSignals.FailureMessage(action.Signal));
            if (sequenceSignals.Contains(action.Signal ?? string.Empty))
                return new ConditionResult(ConditionOutcome.Violation,
                    "Нарушен порядок действий: ожидается «" + sequence[next] + "», получено «" + action.Signal + "».");
            return new ConditionResult(ConditionOutcome.Ignore);
        }

        public void Reset() { next = 0; }
    }

    public sealed class StateSignalCondition : IStepCondition
    {
        private readonly string successSignal;
        private readonly string stateKey;
        private readonly bool requiredValue;
        private readonly HashSet<string> failureSignals;
        private readonly string failureMessage;

        public StateSignalCondition(string successSignal, string stateKey, bool requiredValue, params string[] failureSignals)
            : this(successSignal, stateKey, requiredValue, failureSignals, null)
        {
        }

        public StateSignalCondition(string successSignal, string stateKey, bool requiredValue,
            string[] failureSignals, string failureMessage)
        {
            this.successSignal = ConditionSignals.Required(successSignal, nameof(successSignal));
            this.stateKey = ConditionSignals.Required(stateKey, nameof(stateKey));
            this.requiredValue = requiredValue;
            this.failureSignals = ConditionSignals.Failures(failureSignals, new[] { successSignal });
            this.failureMessage = failureMessage;
        }

        public ConditionResult Evaluate(ScenarioAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            if (failureSignals.Contains(action.Signal ?? string.Empty))
                return new ConditionResult(ConditionOutcome.Violation, ConditionSignals.FailureMessage(action.Signal));
            if (action.Signal != successSignal) return new ConditionResult(ConditionOutcome.Ignore);
            bool state;
            if (action.State == null || !action.State.TryGetValue(stateKey, out state))
                return new ConditionResult(ConditionOutcome.Violation,
                    "Не удалось проверить условие: состояние «" + stateKey + "» не передано из сцены.");
            return state == requiredValue
                ? new ConditionResult(ConditionOutcome.Success)
                : new ConditionResult(ConditionOutcome.Violation,
                    !string.IsNullOrWhiteSpace(failureMessage) ? failureMessage :
                    "Условие не выполнено: для состояния «" + stateKey + "» требуется «" +
                    (requiredValue ? "да" : "нет") + "», получено «" + (state ? "да" : "нет") + "».");
        }

        public void Reset() { }
    }

    internal static class ConditionSignals
    {
        public static string FailureMessage(string signal)
        {
            return signal == "error"
                ? "Имитация ошибки: получен тестовый сигнал «Ошибка»."
                : "Получен настроенный сигнал нарушения «" + signal + "».";
        }

        public static string Required(string signal, string argument)
        {
            if (string.IsNullOrWhiteSpace(signal))
                throw new ArgumentException("Signal and state keys must not be empty.", argument);
            return signal;
        }

        public static HashSet<string> Failures(string[] failures, IEnumerable<string> successSignals)
        {
            var success = new HashSet<string>(successSignals, StringComparer.Ordinal);
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (failures == null) return result;
            foreach (var signal in failures)
            {
                Required(signal, nameof(failures));
                if (success.Contains(signal))
                    throw new ArgumentException("A signal cannot mean both success and failure: " + signal, nameof(failures));
                if (!result.Add(signal))
                    throw new ArgumentException("Duplicate failure signal: " + signal, nameof(failures));
            }
            return result;
        }
    }
}
