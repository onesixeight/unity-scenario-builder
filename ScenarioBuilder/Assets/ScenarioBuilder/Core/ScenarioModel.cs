using System;
using System.Collections.Generic;

namespace ScenarioBuilder.Core
{
    public enum StepStatus { Pending, Active, Completed, Failed, Skipped }
    public enum OutOfOrderPolicy { Violation, Ignore }
    public enum RunMode { Training, Exam }
    public enum ConditionOutcome { Ignore, Progress, Success, Violation }

    public sealed class ScenarioAction
    {
        public string TargetStepId;
        public string Signal;
        public string EventId;
        public Dictionary<string, bool> State = new Dictionary<string, bool>(StringComparer.Ordinal);

        public ScenarioAction(string targetStepId, string signal, string eventId = null)
        {
            TargetStepId = targetStepId;
            Signal = signal;
            EventId = string.IsNullOrWhiteSpace(eventId) ? Guid.NewGuid().ToString("N") : eventId;
        }
    }

    public struct ConditionResult
    {
        public ConditionOutcome Kind;
        public string Message;

        public ConditionResult(ConditionOutcome kind, string message = null)
        {
            Kind = kind;
            Message = message ?? string.Empty;
        }
    }

    public interface IStepCondition
    {
        ConditionResult Evaluate(ScenarioAction action);
        void Reset();
    }

    public sealed class StepSpec
    {
        public string Id { get; }
        public string Title { get; }
        public string Result { get; }
        public Func<IStepCondition> ConditionFactory { get; }

        public StepSpec(string id, string title, string result, Func<IStepCondition> factory)
        {
            Id = id;
            Title = title;
            Result = result;
            ConditionFactory = factory;
        }
    }

    public interface IExecutionPolicy
    {
        RunMode Mode { get; }
        bool RetryOnViolation { get; }
        bool StopGroupAtThreshold { get; }
    }

    public sealed class TrainingExecutionPolicy : IExecutionPolicy
    {
        public RunMode Mode => RunMode.Training;
        public bool RetryOnViolation => true;
        public bool StopGroupAtThreshold => false;
    }

    public sealed class ExamExecutionPolicy : IExecutionPolicy
    {
        public RunMode Mode => RunMode.Exam;
        public bool RetryOnViolation => false;
        public bool StopGroupAtThreshold => true;
    }

    public sealed class ActivationBatch
    {
        public List<string> StepIds = new List<string>();

        public ActivationBatch() { }
        public ActivationBatch(params string[] stepIds)
        {
            if (stepIds != null) StepIds.AddRange(stepIds);
        }
    }

    public sealed class GroupPlan
    {
        public string Id;
        public string Title;
        public int ViolationThreshold = 2;
        public List<string> StepIds = new List<string>();
        public List<ActivationBatch> Batches = new List<ActivationBatch>();
    }

    public sealed class ScenarioPlan
    {
        public List<StepSpec> Steps = new List<StepSpec>();
        public List<GroupPlan> Groups = new List<GroupPlan>();
        public OutOfOrderPolicy OutOfOrderPolicy = OutOfOrderPolicy.Violation;

        public List<string> Validate()
        {
            var errors = new List<string>();
            if (!Enum.IsDefined(typeof(OutOfOrderPolicy), OutOfOrderPolicy))
                errors.Add("Unknown out-of-order policy.");
            if (Steps == null || Steps.Count == 0)
                errors.Add("The scenario needs at least one step.");
            if (Groups == null || Groups.Count == 0)
                errors.Add("The scenario needs at least one group.");

            var stepIds = new HashSet<string>(StringComparer.Ordinal);
            if (Steps != null)
            {
                foreach (var step in Steps)
                {
                    if (step == null)
                    {
                        errors.Add("The step list contains a missing step.");
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(step.Id)) errors.Add("A step has no ID.");
                    else if (!stepIds.Add(step.Id)) errors.Add("Duplicate step ID: " + step.Id);
                    if (step.ConditionFactory == null)
                        errors.Add("Step '" + step.Id + "' has no condition factory.");
                    else
                    {
                        try
                        {
                            if (step.ConditionFactory() == null)
                                errors.Add("Step '" + step.Id + "' creates a missing condition.");
                        }
                        catch (Exception exception)
                        {
                            errors.Add("Invalid condition for '" + step.Id + "': " + exception.Message);
                        }
                    }
                }
            }

            var groupIds = new HashSet<string>(StringComparer.Ordinal);
            var assignedSteps = new HashSet<string>(StringComparer.Ordinal);
            if (Groups != null)
            {
                foreach (var group in Groups)
                {
                    if (group == null)
                    {
                        errors.Add("The group list contains a missing group.");
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(group.Id)) errors.Add("A group has no ID.");
                    else if (!groupIds.Add(group.Id)) errors.Add("Duplicate group ID: " + group.Id);
                    if (group.ViolationThreshold < 1)
                        errors.Add("Group '" + group.Id + "' needs a positive violation threshold.");
                    var membership = new HashSet<string>(StringComparer.Ordinal);
                    if (group.StepIds == null || group.StepIds.Count == 0)
                        errors.Add("Group '" + group.Id + "' has no members.");
                    else
                    {
                        foreach (var id in group.StepIds)
                        {
                            if (string.IsNullOrWhiteSpace(id) || !stepIds.Contains(id))
                                errors.Add("Group '" + group.Id + "' references unknown step '" + id + "'.");
                            if (!membership.Add(id))
                                errors.Add("Group '" + group.Id + "' repeats step '" + id + "'.");
                            if (!assignedSteps.Add(id))
                                errors.Add("Step '" + id + "' belongs to more than one group.");
                        }
                    }

                    var scheduledSteps = new HashSet<string>(StringComparer.Ordinal);
                    if (group.Batches == null || group.Batches.Count == 0)
                        errors.Add("Group '" + group.Id + "' has no activation batches.");
                    else
                    {
                        foreach (var batch in group.Batches)
                        {
                            if (batch == null || batch.StepIds == null || batch.StepIds.Count == 0)
                            {
                                errors.Add("Group '" + group.Id + "' contains an empty activation batch.");
                                continue;
                            }
                            foreach (var id in batch.StepIds)
                            {
                                if (string.IsNullOrWhiteSpace(id) || !membership.Contains(id))
                                    errors.Add("A batch of '" + group.Id + "' references a nonmember step '" + id + "'.");
                                if (!scheduledSteps.Add(id))
                                    errors.Add("Step '" + id + "' appears in multiple activation positions.");
                            }
                        }
                    }
                    foreach (var id in membership)
                        if (!scheduledSteps.Contains(id))
                            errors.Add("Group '" + group.Id + "' does not activate step '" + id + "'.");
                }
            }
            foreach (var id in stepIds)
                if (!assignedSteps.Contains(id)) errors.Add("Step '" + id + "' has no group.");
            return errors;
        }
    }

    public sealed class StepRuntime : IStepProgress
    {
        public StepSpec Spec { get; }
        public StepStatus Status { get; internal set; } = StepStatus.Pending;
        public int Attempts { get; internal set; }
        public int Violations { get; internal set; }
        public string ProgressMessage { get; internal set; } = string.Empty;
        internal IStepCondition Condition { get; }
        internal bool HasOpenAttempt { get; set; }

        internal StepRuntime(StepSpec spec)
        {
            Spec = spec;
            Condition = spec.ConditionFactory();
            if (Condition == null) throw new ArgumentException("Missing condition for '" + spec.Id + "'.");
            Condition.Reset();
        }
    }
}
