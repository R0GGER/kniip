using System.Collections.Generic;
using System.Linq;
using Kniip.Resources;
using Kniip.Upload;

namespace Kniip.Workflows;

internal static class WorkflowValidator
{
    /// <summary>Wat er mis is met een workflow; leeg betekent dat hij kan draaien.</summary>
    public static List<string> Problems(WorkflowDefinition workflow, IReadOnlyList<UploadDestination> uploaders)
    {
        var problems = new List<string>();
        List<WorkflowStep> steps = workflow.Steps;
        if (steps.Count == 0 || !WorkflowStep.IsCapture(steps[0].Kind))
        {
            problems.Add(Strings.Workflow_ProblemFirstCapture);
        }

        if (steps.Skip(1).Any(s => WorkflowStep.IsCapture(s.Kind)))
        {
            problems.Add(Strings.Workflow_ProblemCaptureLater);
        }

        if (steps.Count(s => s.Kind == WorkflowStepKind.Edit) > 1)
        {
            problems.Add(Strings.Workflow_ProblemOneEdit);
        }

        bool uploaded = false;
        foreach (WorkflowStep step in steps)
        {
            switch (step.Kind)
            {
                case WorkflowStepKind.Upload:
                    uploaded = true;
                    bool configured = step.UploaderId is { } id
                        ? uploaders.Any(u => u.Id == id && u.IsConfigured)
                        : uploaders.Any(u => u.IsConfigured);
                    if (!configured)
                    {
                        problems.Add(Strings.Workflow_ProblemNoUploader);
                    }

                    break;
                case WorkflowStepKind.CopyUrl when !uploaded:
                    problems.Add(Strings.Workflow_ProblemCopyUrl);
                    break;
                case WorkflowStepKind.OpenWith when string.IsNullOrWhiteSpace(step.ProgramPath):
                    problems.Add(Strings.Workflow_ProblemNoProgram);
                    break;
            }
        }

        return problems.Distinct().ToList();
    }
}
