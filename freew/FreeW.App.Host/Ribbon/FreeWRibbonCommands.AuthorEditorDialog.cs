using System.Windows;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Host;

internal static partial class FreeWRibbonCommands
{
    private static class AuthorEditorDialog
    {
        private sealed record RowControls(
            System.Windows.Controls.TextBox First,
            System.Windows.Controls.TextBox Middle,
            System.Windows.Controls.TextBox Last,
            System.Windows.Controls.Grid Host);

        public static SourceManagementAuthorEditorState? Ask(Window? owner, SourceManagementSourceEntry entry)
        {
            var session = new SourceManagementAuthorEditorSession(entry);
            var initial = session.CurrentPlan;
            var text = SourceManagementDialogPlanner.ResolveText(UiText.Get);
            SourceManagementAuthorEditorState? result = null;

            var dialog = new FreeWDialogWindow
            {
                Title = SourceManagementDialogPlanner.PrimaryAuthorEditorTitle,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false
            };

            var personalMode = new System.Windows.Controls.RadioButton
            {
                Content = SourceManagementDialogPlanner.PersonalAuthorModeLabel,
                GroupName = "PrimaryAuthorMode",
                IsChecked = initial.Mode == SourceManagementAuthorEditorMode.Personal,
                Margin = new Thickness(
                    0,
                    0,
                    0,
                    SourceManagementAuthorEditorVisualMetrics.PersonalModeBottomMargin)
            };
            var corporateMode = new System.Windows.Controls.RadioButton
            {
                Content = SourceManagementDialogPlanner.CorporateAuthorModeLabel,
                GroupName = "PrimaryAuthorMode",
                IsChecked = initial.Mode == SourceManagementAuthorEditorMode.Corporate,
                Margin = new Thickness(
                    0,
                    SourceManagementAuthorEditorVisualMetrics.CorporateModeTopMargin,
                    0,
                    SourceManagementAuthorEditorVisualMetrics.PersonalModeBottomMargin)
            };
            var peoplePanel = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(
                    SourceManagementAuthorEditorVisualMetrics.PeoplePanelIndent,
                    0,
                    0,
                    0)
            };
            var rowsPanel = new System.Windows.Controls.StackPanel();
            var corporateLabel = new System.Windows.Controls.TextBlock
            {
                Text = SourceManagementDialogPlanner.CorporateAuthorLabel,
                Margin = new Thickness(
                    SourceManagementAuthorEditorVisualMetrics.PeoplePanelIndent,
                    0,
                    0,
                    SourceManagementAuthorEditorVisualMetrics.CorporateLabelBottomMargin)
            };
            var corporateBox = NewAuthorTextBox(
                initial.CorporateAuthor,
                minWidth: SourceManagementAuthorEditorVisualMetrics.CorporateFieldMinimumWidth);

            var personRows = new SourceManagementAuthorRowCollection<RowControls>(
                row =>
                {
                    var grid = CreatePersonRowGrid();
                    var first = NewAuthorTextBox(row.First);
                    var middle = NewAuthorTextBox(row.Middle);
                    var last = NewAuthorTextBox(
                        row.Last,
                        minWidth: SourceManagementAuthorEditorVisualMetrics.LastNameFieldMinimumWidth);
                    AddGridChild(grid, first, 0);
                    AddGridChild(grid, middle, 1);
                    AddGridChild(grid, last, 2);
                    return new RowControls(first, middle, last, grid);
                },
                row => new SourceManagementAuthorPersonRow(
                    row.First.Text ?? string.Empty,
                    row.Middle.Text ?? string.Empty,
                    row.Last.Text ?? string.Empty),
                row => rowsPanel.Children.Add(row.Host),
                () => rowsPanel.Children.Clear());

            void ApplyMode(SourceManagementAuthorEditorPlan plan)
            {
                peoplePanel.IsEnabled = plan.PersonalAuthorFieldsEnabled;
                corporateLabel.IsEnabled = plan.CorporateAuthorFieldEnabled;
                corporateBox.IsEnabled = plan.CorporateAuthorFieldEnabled;
            }

            personRows.Render(initial.PersonalRows);

            var header = CreatePersonRowGrid();
            AddGridChild(header, NewHeader(SourceManagementDialogPlanner.AuthorFirstNameLabel), 0);
            AddGridChild(header, NewHeader(SourceManagementDialogPlanner.AuthorMiddleNameLabel), 1);
            AddGridChild(header, NewHeader(SourceManagementDialogPlanner.AuthorLastNameLabel), 2);
            peoplePanel.Children.Add(header);
            peoplePanel.Children.Add(rowsPanel);

            var addRow = new System.Windows.Controls.Button
            {
                Content = SourceManagementDialogPlanner.AddAuthorRowButtonLabel,
                MinWidth = SourceManagementAuthorEditorVisualMetrics.ButtonMinimumWidth,
                Margin = new Thickness(
                    0,
                    SourceManagementAuthorEditorVisualMetrics.InlineActionTopMargin,
                    SourceManagementAuthorEditorVisualMetrics.ActionSpacing,
                    0)
            };
            addRow.Click += (_, _) => personRows.Render(session.AddPersonalAuthorRow(
                personRows.Read(),
                corporateBox.Text).PersonalRows);
            var removeRow = new System.Windows.Controls.Button
            {
                Content = SourceManagementDialogPlanner.RemoveAuthorRowButtonLabel,
                MinWidth = SourceManagementAuthorEditorVisualMetrics.ButtonMinimumWidth,
                Margin = new Thickness(
                    0,
                    SourceManagementAuthorEditorVisualMetrics.InlineActionTopMargin,
                    0,
                    0)
            };
            removeRow.Click += (_, _) => personRows.Render(session.RemoveFinalPersonalAuthorRow(
                personRows.Read(),
                corporateBox.Text).PersonalRows);
            peoplePanel.Children.Add(new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Children = { addRow, removeRow }
            });

            personalMode.Checked += (_, _) => ApplyMode(session.SelectMode(
                SourceManagementAuthorEditorMode.Personal,
                personRows.Read(),
                corporateBox.Text));
            corporateMode.Checked += (_, _) => ApplyMode(session.SelectMode(
                SourceManagementAuthorEditorMode.Corporate,
                personRows.Read(),
                corporateBox.Text));

            var ok = new System.Windows.Controls.Button
            {
                Content = text.OkButtonLabel,
                IsDefault = true,
                MinWidth = SourceManagementAuthorEditorVisualMetrics.ButtonMinimumWidth,
                Margin = new Thickness(
                    0,
                    0,
                    SourceManagementAuthorEditorVisualMetrics.ActionSpacing,
                    0)
            };
            var cancel = new System.Windows.Controls.Button
            {
                Content = text.CancelButtonLabel,
                IsCancel = true,
                MinWidth = SourceManagementAuthorEditorVisualMetrics.ButtonMinimumWidth
            };
            ok.Click += (_, _) =>
            {
                result = session.Accept(personRows.Read(), corporateBox.Text);
                dialog.DialogResult = true;
            };

            var buttons = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(
                    0,
                    SourceManagementAuthorEditorVisualMetrics.DialogActionTopMargin,
                    0,
                    0)
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var panel = new System.Windows.Controls.StackPanel
            {
                Margin = new Thickness(SourceManagementAuthorEditorVisualMetrics.BodyInset)
            };
            panel.Children.Add(personalMode);
            panel.Children.Add(peoplePanel);
            panel.Children.Add(corporateMode);
            panel.Children.Add(corporateLabel);
            panel.Children.Add(corporateBox);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            ApplyMode(initial);
            return dialog.ShowDialog() == true ? result : null;
        }

        private static System.Windows.Controls.Grid CreatePersonRowGrid()
        {
            var grid = new System.Windows.Controls.Grid
            {
                Margin = new Thickness(
                    0,
                    0,
                    0,
                    SourceManagementAuthorEditorVisualMetrics.PersonRowBottomMargin)
            };
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            {
                Width = new GridLength(SourceManagementAuthorEditorVisualMetrics.FirstNameColumnWidth)
            });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            {
                Width = new GridLength(SourceManagementAuthorEditorVisualMetrics.MiddleNameColumnWidth)
            });
            grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition
            {
                Width = new GridLength(SourceManagementAuthorEditorVisualMetrics.LastNameColumnWidth)
            });
            return grid;
        }

        private static System.Windows.Controls.TextBlock NewHeader(string text) =>
            new()
            {
                Text = text,
                Margin = new Thickness(
                    0,
                    0,
                    SourceManagementAuthorEditorVisualMetrics.HeaderRightMargin,
                    SourceManagementAuthorEditorVisualMetrics.HeaderBottomMargin)
            };

        private static System.Windows.Controls.TextBox NewAuthorTextBox(
            string? text,
            double minWidth = SourceManagementAuthorEditorVisualMetrics.DefaultNameFieldMinimumWidth) =>
            new()
            {
                Text = text ?? string.Empty,
                MinWidth = minWidth,
                Margin = new Thickness(
                    0,
                    0,
                    SourceManagementAuthorEditorVisualMetrics.FieldRightMargin,
                    0)
            };

        private static void AddGridChild(
            System.Windows.Controls.Grid grid,
            UIElement child,
            int column)
        {
            System.Windows.Controls.Grid.SetColumn(child, column);
            grid.Children.Add(child);
        }
    }
}
