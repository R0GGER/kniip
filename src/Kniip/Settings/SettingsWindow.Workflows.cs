using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Kniip.Infrastructure;
using Kniip.Resources;
using Kniip.Upload;
using Kniip.Workflows;

namespace Kniip.Settings;

/// <summary>Tabblad Workflows: eigen workflows met naam, icoon, sneltoets en stappen.</summary>
public partial class SettingsWindow
{
    private static readonly string[] WorkflowGlyphs =
    {
        "\uE945", "\uF407", "\uE722", "\uE714", "\uF4A9", "\uE91B", "\uE70F", "\uE74E",
        "\uE8C8", "\uE898", "\uE753", "\uE71B", "\uE72D", "\uE715", "\uE774", "\uE838",
        "\uE7AC", "\uE8A7", "\uE734", "\uE768", "\uE713", "\uE8F1", "\uE77F", "\uE7C3"
    };

    private static readonly WorkflowStepKind[] CaptureKinds =
    {
        WorkflowStepKind.CaptureRegion, WorkflowStepKind.CaptureWindow, WorkflowStepKind.RecordMp4,
        WorkflowStepKind.RecordGif, WorkflowStepKind.FromClipboard
    };

    private static readonly WorkflowStepKind[] ActionKinds =
    {
        WorkflowStepKind.Edit, WorkflowStepKind.Save, WorkflowStepKind.Copy, WorkflowStepKind.Upload,
        WorkflowStepKind.CopyUrl, WorkflowStepKind.OpenWith, WorkflowStepKind.OpenFolder
    };

    private sealed record WorkflowRow(WorkflowDefinition Workflow, bool IsSelected, string Title, string Summary, string Glyph,
        ImageSource? Icon, string? Hotkey, string? Problem)
    {
        public bool HasProblem => Problem != null;
    }

    private sealed record StepRow(int Index, bool IsSelected, string Number, string Glyph, string Title);

    private WorkflowStep? SelectedStep =>
        _selectedWorkflow is { } workflow && _selectedStep >= 0 && _selectedStep < workflow.Steps.Count ? workflow.Steps[_selectedStep] : null;

    /// <summary>Opent dit venster op het tabblad Workflows.</summary>
    public void ShowWorkflowsTab() => WorkflowsTab.IsSelected = true;

    private static string? HotkeyText(string hotkey) =>
        HotkeyGesture.TryParse(hotkey, out HotkeyGesture gesture) ? gesture.DisplayText : null;

    private void ShowWorkflows()
    {
        WorkflowList.ItemsSource = _workflows.Select(w =>
        {
            ImageSource? icon = WorkflowBarWindow.LoadIconFile(w.IconFile);
            string summary = w.Steps.Count == 0 ? Strings.Settings_WorkflowNoSteps : w.Summary(_uploaders);
            return new WorkflowRow(w, w == _selectedWorkflow, w.Title, summary, icon == null ? w.Glyph : string.Empty, icon,
                HotkeyText(w.Hotkey), WorkflowValidator.Problems(w, _uploaders).FirstOrDefault());
        }).ToList();
        NoWorkflowsText.Visibility = _workflows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        int index = _selectedWorkflow == null ? -1 : _workflows.IndexOf(_selectedWorkflow);
        DuplicateWorkflowButton.IsEnabled = index >= 0;
        RemoveWorkflowButton.IsEnabled = index >= 0;
        WorkflowUpButton.IsEnabled = index > 0;
        WorkflowDownButton.IsEnabled = index >= 0 && index < _workflows.Count - 1;
        ScheduleSave();
    }

    private void SelectWorkflow(WorkflowDefinition? workflow)
    {
        StopListening();
        _selectedWorkflow = workflow;
        _selectedStep = workflow is { Steps.Count: > 0 } ? 0 : -1;
        WorkflowDetails.Visibility = workflow == null ? Visibility.Collapsed : Visibility.Visible;
        if (workflow != null)
        {
            _fillingWorkflow = true;
            WorkflowNameBox.Text = workflow.Name;
            WorkflowTrayBox.IsChecked = workflow.ShowInTray;
            WorkflowBarBox.IsChecked = workflow.ShowInBar;
            WorkflowHotkeyButton.Tag = WorkflowHotkeyPrefix + workflow.Id;
            WorkflowHotkeyClearButton.Tag = WorkflowHotkeyButton.Tag;
            _fillingWorkflow = false;
            ShowHotkey(WorkflowHotkeyPrefix + workflow.Id);
            ShowWorkflowIcon();
        }

        ShowWorkflows();
        ShowSteps();
    }

    private void ShowWorkflowIcon()
    {
        if (_selectedWorkflow is not { } workflow)
        {
            return;
        }

        ImageSource? icon = WorkflowBarWindow.LoadIconFile(workflow.IconFile);
        WorkflowIconImage.Source = icon;
        WorkflowIconGlyph.Text = icon == null ? workflow.Glyph : string.Empty;
    }

    private void WorkflowRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is WorkflowRow row && row.Workflow != _selectedWorkflow)
        {
            SelectWorkflow(row.Workflow);
        }
    }

    private void NewWorkflow_Click(object sender, RoutedEventArgs e)
    {
        var workflow = new WorkflowDefinition
        {
            Name = Strings.Settings_WorkflowNewName,
            Steps =
            {
                new WorkflowStep { Kind = WorkflowStepKind.CaptureRegion },
                new WorkflowStep { Kind = WorkflowStepKind.Save }
            }
        };
        _workflows.Add(workflow);
        SelectWorkflow(workflow);
        WorkflowNameBox.Focus();
        WorkflowNameBox.SelectAll();
    }

    private void DuplicateWorkflow_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkflow is not { } workflow)
        {
            return;
        }

        WorkflowDefinition copy = workflow.Clone();
        copy.Id = Guid.NewGuid();
        copy.Name = string.Format(Strings.Settings_UploadCopyName, workflow.Title);
        // Twee workflows op één sneltoets kan niet.
        copy.Hotkey = string.Empty;
        _workflows.Insert(_workflows.IndexOf(workflow) + 1, copy);
        SelectWorkflow(copy);
    }

    private void MoveWorkflow_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkflow is not { } workflow || sender is not FrameworkElement { Tag: string tag })
        {
            return;
        }

        int index = _workflows.IndexOf(workflow);
        int target = index + int.Parse(tag, System.Globalization.CultureInfo.InvariantCulture);
        if (target < 0 || target >= _workflows.Count)
        {
            return;
        }

        _workflows.RemoveAt(index);
        _workflows.Insert(target, workflow);
        ShowWorkflows();
    }

    private void RemoveWorkflow_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkflow is not { } workflow)
        {
            return;
        }

        if (MessageBox.Show(this, string.Format(Strings.Settings_WorkflowRemoveConfirm, workflow.Title), "kniip",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        int index = _workflows.IndexOf(workflow);
        _workflows.Remove(workflow);
        SelectWorkflow(_workflows.Count == 0 ? null : _workflows[Math.Min(index, _workflows.Count - 1)]);
    }

    private void WorkflowName_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_fillingWorkflow || _selectedWorkflow is not { } workflow)
        {
            return;
        }

        workflow.Name = WorkflowNameBox.Text;
        ShowWorkflows();
    }

    private void WorkflowOption_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkflow is not { } workflow)
        {
            return;
        }

        workflow.ShowInTray = WorkflowTrayBox.IsChecked == true;
        workflow.ShowInBar = WorkflowBarBox.IsChecked == true;
    }

    /// <summary>Raster met symbolen, plus een eigen afbeelding kiezen.</summary>
    private void WorkflowIcon_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkflow is not { } workflow)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = WorkflowIconButton, Placement = PlacementMode.Bottom };
        var grid = new UniformGrid { Columns = 8 };
        foreach (string glyph in WorkflowGlyphs)
        {
            var text = new TextBlock { Text = glyph, FontSize = 16 };
            text.SetResourceReference(StyleProperty, "IconText");
            var button = new Button { Content = text, MinWidth = 32, MinHeight = 32, Margin = new Thickness(1) };
            button.SetResourceReference(StyleProperty, "FlatButton");
            if (workflow.IconFile == null && workflow.Glyph == glyph)
            {
                button.SetResourceReference(BackgroundProperty, "HoverBrush");
            }

            button.Click += (_, _) =>
            {
                menu.IsOpen = false;
                workflow.Glyph = glyph;
                workflow.IconFile = null;
                ShowWorkflowIcon();
                ShowWorkflows();
            };
            grid.Children.Add(button);
        }

        menu.Items.Add(new MenuItem { Header = grid, StaysOpenOnClick = true, Padding = new Thickness(0) });
        menu.Items.Add(new Separator());
        var file = new MenuItem { Header = Strings.Settings_WorkflowIconFile };
        file.Click += (_, _) => ChooseWorkflowIconFile(workflow);
        menu.Items.Add(file);
        if (workflow.IconFile != null)
        {
            var clear = new MenuItem { Header = Strings.Settings_WorkflowIconClear };
            clear.Click += (_, _) =>
            {
                workflow.IconFile = null;
                ShowWorkflowIcon();
                ShowWorkflows();
            };
            menu.Items.Add(clear);
        }

        menu.IsOpen = true;
    }

    private void ChooseWorkflowIconFile(WorkflowDefinition workflow)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.Settings_WorkflowIconFile,
            Filter = Strings.Settings_WorkflowIconFilter
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (WorkflowBarWindow.LoadIconFile(dialog.FileName) == null)
        {
            MessageBox.Show(this, string.Format(Strings.Settings_WorkflowIconInvalid, System.IO.Path.GetFileName(dialog.FileName)),
                "kniip", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        workflow.IconFile = dialog.FileName;
        ShowWorkflowIcon();
        ShowWorkflows();
    }

    // ---------- Stappen ----------

    private void ShowSteps()
    {
        if (_selectedWorkflow is not { } workflow)
        {
            StepList.ItemsSource = null;
            return;
        }

        StepList.ItemsSource = workflow.Steps.Select((step, i) => new StepRow(i, i == _selectedStep,
            (i + 1).ToString(System.Globalization.CultureInfo.CurrentCulture) + ".", WorkflowStep.Glyph(step.Kind), step.Describe(_uploaders))).ToList();

        WorkflowStep? selected = SelectedStep;
        // De opname staat altijd bovenaan; acties kunnen er niet boven.
        bool firstIsCapture = workflow.Steps.Count > 0 && WorkflowStep.IsCapture(workflow.Steps[0].Kind);
        int floor = firstIsCapture && selected != null && !WorkflowStep.IsCapture(selected.Kind) ? 1 : 0;
        StepUpButton.IsEnabled = selected != null && _selectedStep > floor;
        StepDownButton.IsEnabled = selected != null && !WorkflowStep.IsCapture(selected.Kind) && _selectedStep < workflow.Steps.Count - 1;
        RemoveStepButton.IsEnabled = selected != null;
        ShowStepDetails();
        ShowWorkflowProblems();
    }

    private void ShowWorkflowProblems()
    {
        List<string> problems = _selectedWorkflow == null ? new List<string>() : WorkflowValidator.Problems(_selectedWorkflow, _uploaders);
        WorkflowProblemsText.Text = string.Join("\n", problems);
        WorkflowProblemsPanel.Visibility = problems.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ShowStepDetails()
    {
        WorkflowStep? step = SelectedStep;
        _fillingWorkflow = true;
        StepUploadPanel.Visibility = step?.Kind == WorkflowStepKind.Upload ? Visibility.Visible : Visibility.Collapsed;
        StepAskBox.Visibility = step?.Kind == WorkflowStepKind.Save ? Visibility.Visible : Visibility.Collapsed;
        StepOpenWithPanel.Visibility = step?.Kind == WorkflowStepKind.OpenWith ? Visibility.Visible : Visibility.Collapsed;
        string? hint = step?.Kind switch
        {
            WorkflowStepKind.Edit => Strings.Settings_WorkflowEditHint,
            WorkflowStepKind.Save => Strings.Settings_WorkflowSaveHint,
            WorkflowStepKind.OpenFolder => Strings.Settings_WorkflowOpenFolderHint,
            WorkflowStepKind.RecordMp4 or WorkflowStepKind.RecordGif => Strings.Settings_WorkflowRecordHint,
            _ => null
        };
        StepHint.Text = hint ?? string.Empty;
        StepHint.Visibility = hint == null ? Visibility.Collapsed : Visibility.Visible;

        if (step?.Kind == WorkflowStepKind.Upload)
        {
            StepUploaderCombo.Items.Clear();
            string? standard = _uploaders.FirstOrDefault(u => u.Id == _kindUploaderIds[UploadKind.Media])?.Title
                               ?? _uploaders.FirstOrDefault(u => u.Id == _defaultUploaderId)?.Title;
            var defaultItem = new ComboBoxItem
            {
                Content = standard == null ? Strings.Settings_UploadDefault : string.Format(Strings.Settings_UploadKindDefault, standard),
                Tag = null
            };
            StepUploaderCombo.Items.Add(defaultItem);
            StepUploaderCombo.SelectedItem = defaultItem;
            foreach (UploadDestination destination in _uploaders)
            {
                var item = new ComboBoxItem { Content = destination.Title, Tag = destination.Id };
                StepUploaderCombo.Items.Add(item);
                if (destination.Id == step.UploaderId)
                {
                    StepUploaderCombo.SelectedItem = item;
                }
            }
        }

        StepAskBox.IsChecked = step?.AskLocation == true;
        StepProgramBox.Text = step?.ProgramPath ?? string.Empty;
        StepArgumentsBox.Text = step?.Arguments ?? string.Empty;
        _fillingWorkflow = false;
    }

    /// <summary>Na een wijziging aan een stap: de lijsten bijwerken zonder de invoervelden opnieuw te vullen.</summary>
    private void StepChanged()
    {
        if (_selectedWorkflow is not { } workflow)
        {
            return;
        }

        StepList.ItemsSource = workflow.Steps.Select((step, i) => new StepRow(i, i == _selectedStep,
            (i + 1).ToString(System.Globalization.CultureInfo.CurrentCulture) + ".", WorkflowStep.Glyph(step.Kind), step.Describe(_uploaders))).ToList();
        ShowWorkflows();
        ShowWorkflowProblems();
    }

    private void StepRow_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is StepRow row && row.Index != _selectedStep)
        {
            _selectedStep = row.Index;
            ShowSteps();
        }
    }

    private void AddStep_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkflow is not { } workflow)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = AddStepButton, Placement = PlacementMode.Bottom };
        var captures = new MenuItem { Header = Strings.Settings_WorkflowCaptureGroup, IsEnabled = false };
        menu.Items.Add(captures);
        foreach (WorkflowStepKind kind in CaptureKinds)
        {
            menu.Items.Add(StepMenuItem(workflow, kind));
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = Strings.Settings_WorkflowActionGroup, IsEnabled = false });
        foreach (WorkflowStepKind kind in ActionKinds)
        {
            menu.Items.Add(StepMenuItem(workflow, kind));
        }

        menu.IsOpen = true;
    }

    private MenuItem StepMenuItem(WorkflowDefinition workflow, WorkflowStepKind kind)
    {
        var icon = new TextBlock { Text = WorkflowStep.Glyph(kind), FontSize = 14 };
        icon.SetResourceReference(StyleProperty, "IconText");
        var item = new MenuItem { Header = WorkflowStep.Name(kind), Icon = icon };
        item.Click += (_, _) => AddStep(workflow, kind);
        return item;
    }

    /// <summary>Een opname komt bovenaan (en vervangt de bestaande), een actie na de geselecteerde stap.</summary>
    private void AddStep(WorkflowDefinition workflow, WorkflowStepKind kind)
    {
        List<WorkflowStep> steps = workflow.Steps;
        bool firstIsCapture = steps.Count > 0 && WorkflowStep.IsCapture(steps[0].Kind);
        if (WorkflowStep.IsCapture(kind))
        {
            if (firstIsCapture)
            {
                steps[0].Kind = kind;
            }
            else
            {
                steps.Insert(0, new WorkflowStep { Kind = kind });
            }

            _selectedStep = 0;
        }
        else
        {
            int index = Math.Max(_selectedStep < 0 ? steps.Count : _selectedStep + 1, firstIsCapture ? 1 : 0);
            steps.Insert(Math.Min(index, steps.Count), new WorkflowStep { Kind = kind });
            _selectedStep = Math.Min(index, steps.Count - 1);
        }

        ShowWorkflows();
        ShowSteps();
    }

    private void MoveStep_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkflow is not { } workflow || SelectedStep is not { } step || sender is not FrameworkElement { Tag: string tag })
        {
            return;
        }

        int target = _selectedStep + int.Parse(tag, System.Globalization.CultureInfo.InvariantCulture);
        if (target < 0 || target >= workflow.Steps.Count)
        {
            return;
        }

        workflow.Steps.RemoveAt(_selectedStep);
        workflow.Steps.Insert(target, step);
        _selectedStep = target;
        ShowWorkflows();
        ShowSteps();
    }

    private void RemoveStep_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkflow is not { } workflow || SelectedStep == null)
        {
            return;
        }

        workflow.Steps.RemoveAt(_selectedStep);
        _selectedStep = Math.Min(_selectedStep, workflow.Steps.Count - 1);
        ShowWorkflows();
        ShowSteps();
    }

    private void StepUploader_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        e.Handled = true;
        if (_fillingWorkflow || SelectedStep is not { } step)
        {
            return;
        }

        step.UploaderId = StepUploaderCombo.SelectedItem is ComboBoxItem { Tag: Guid id } ? id : null;
        StepChanged();
    }

    private void StepAsk_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedStep is { } step)
        {
            step.AskLocation = StepAskBox.IsChecked == true;
            StepChanged();
        }
    }

    private void StepProgram_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_fillingWorkflow && SelectedStep is { } step)
        {
            step.ProgramPath = string.IsNullOrWhiteSpace(StepProgramBox.Text) ? null : StepProgramBox.Text.Trim();
            StepChanged();
        }
    }

    private void StepArguments_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_fillingWorkflow && SelectedStep is { } step)
        {
            step.Arguments = StepArgumentsBox.Text;
        }
    }

    private void BrowseProgram_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Strings.Settings_WorkflowProgram,
            Filter = Strings.Settings_WorkflowProgramFilter,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };
        if (dialog.ShowDialog(this) == true)
        {
            StepProgramBox.Text = dialog.FileName;
        }
    }
}
