using System.Collections.Generic;
using System.Linq;
using Kniip.Resources;
using Kniip.Upload;
using Kniip.Watermark;

namespace Kniip.Workflows;

internal static class WorkflowValidator
{
    /// <summary>Wat er mis is met een workflow; leeg betekent dat hij kan draaien.</summary>
    /// <param name="watermark">Het ingestelde watermerk, om te melden dat de stap Watermerk niets te tekenen heeft.</param>
    public static List<string> Problems(WorkflowDefinition workflow, IReadOnlyList<UploadDestination> uploaders, WatermarkSettings? watermark = null)
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

        int watermarkIndex = steps.FindIndex(s => s.Kind == WorkflowStepKind.Watermark);
        if (watermarkIndex >= 0)
        {
            if (steps.Count(s => s.Kind == WorkflowStepKind.Watermark) > 1)
            {
                problems.Add(Strings.Workflow_ProblemOneWatermark);
            }

            if (watermark != null && !WatermarkRenderer.IsUsable(watermark))
            {
                problems.Add(Strings.Workflow_ProblemWatermarkNotSet);
            }

            if (steps.Take(watermarkIndex).Any(s => IsOutput(s.Kind)))
            {
                problems.Add(Strings.Workflow_ProblemWatermarkLate);
            }
            else if (!steps.Skip(watermarkIndex + 1).Any(s => s.Kind == WorkflowStepKind.Edit || IsOutput(s.Kind)))
            {
                problems.Add(Strings.Workflow_ProblemWatermarkUnused);
            }
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

    /// <summary>Stappen die het knipsel of de screencast doorgeven; een watermerk daarna komt er niet meer op.</summary>
    private static bool IsOutput(WorkflowStepKind kind) => kind is WorkflowStepKind.Save or WorkflowStepKind.Copy
        or WorkflowStepKind.Upload or WorkflowStepKind.OpenWith or WorkflowStepKind.OpenFolder;
}
