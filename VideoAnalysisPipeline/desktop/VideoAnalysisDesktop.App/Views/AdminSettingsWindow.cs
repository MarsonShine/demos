using System;
using System.Windows;
using System.Windows.Controls;
using VideoAnalysisDesktop.Infrastructure.Security;

namespace VideoAnalysisDesktop.App.Views;

/// <summary>
/// Allows an authorized operator to update the encrypted service credentials.
/// The API key is deliberately never shown after it has been saved.
/// </summary>
public sealed class AdminSettingsWindow : Window
{
    private readonly SecretManager _secretManager;
    private readonly TextBox _endpointTextBox = new();
    private readonly PasswordBox _apiKeyPasswordBox = new();
    private readonly TextBox _deploymentTextBox = new();
    private readonly TextBlock _keyStatusTextBlock = new();
    private string? _existingApiKey;

    public AdminSettingsWindow(SecretManager secretManager)
    {
        _secretManager = secretManager;

        Title = "Admin Settings";
        Width = 500;
        MinWidth = 500;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        Content = BuildContent();
        LoadExistingValues();
    }

    private UIElement BuildContent()
    {
        var panel = new Grid { Margin = new Thickness(20) };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        for (var i = 0; i < 5; i++)
        {
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        AddLabel(panel, "Azure endpoint:", 0);
        _endpointTextBox.MinWidth = 320;
        _endpointTextBox.Margin = new Thickness(12, 0, 0, 8);
        Grid.SetRow(_endpointTextBox, 0);
        Grid.SetColumn(_endpointTextBox, 1);
        panel.Children.Add(_endpointTextBox);

        AddLabel(panel, "API key:", 1);
        _apiKeyPasswordBox.Margin = new Thickness(12, 0, 0, 2);
        Grid.SetRow(_apiKeyPasswordBox, 1);
        Grid.SetColumn(_apiKeyPasswordBox, 1);
        panel.Children.Add(_apiKeyPasswordBox);

        _keyStatusTextBlock.Margin = new Thickness(12, 0, 0, 8);
        _keyStatusTextBlock.FontSize = 11;
        Grid.SetRow(_keyStatusTextBlock, 2);
        Grid.SetColumn(_keyStatusTextBlock, 1);
        panel.Children.Add(_keyStatusTextBlock);

        AddLabel(panel, "Deployment:", 3);
        _deploymentTextBox.Margin = new Thickness(12, 0, 0, 16);
        Grid.SetRow(_deploymentTextBox, 3);
        Grid.SetColumn(_deploymentTextBox, 1);
        panel.Children.Add(_deploymentTextBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancelButton = new Button { Content = "Cancel", IsCancel = true, MinWidth = 88, Margin = new Thickness(0, 0, 8, 0) };
        cancelButton.Click += (_, _) => DialogResult = false;
        var saveButton = new Button { Content = "Save", IsDefault = true, MinWidth = 88 };
        saveButton.Click += OnSaveClicked;
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(saveButton);
        Grid.SetRow(buttons, 4);
        Grid.SetColumnSpan(buttons, 2);
        panel.Children.Add(buttons);

        return panel;
    }

    private static void AddLabel(Grid panel, string content, int row)
    {
        var label = new Label { Content = content, VerticalContentAlignment = VerticalAlignment.Center };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, 0);
        panel.Children.Add(label);
    }

    private void LoadExistingValues()
    {
        try
        {
            var existing = _secretManager.Load();
            if (existing is null)
            {
                _keyStatusTextBlock.Text = "Enter an API key to configure this installation.";
                return;
            }

            _endpointTextBox.Text = existing.Endpoint;
            _deploymentTextBox.Text = existing.Deployment;
            _existingApiKey = existing.ApiKey;
            _keyStatusTextBlock.Text = "An API key is already configured. Leave this field empty to keep it unchanged.";
        }
        catch
        {
            _keyStatusTextBlock.Text = "Existing credentials could not be read. Enter all values to replace them.";
        }
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        var endpoint = _endpointTextBox.Text.Trim();
        var deployment = _deploymentTextBox.Text.Trim();
        var enteredApiKey = _apiKeyPasswordBox.Password.Trim();
        var apiKey = string.IsNullOrEmpty(enteredApiKey) ? _existingApiKey : enteredApiKey;

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) || endpointUri.Scheme != Uri.UriSchemeHttps)
        {
            MessageBox.Show(this, "Enter a valid HTTPS Azure endpoint.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(deployment) || string.IsNullOrWhiteSpace(apiKey))
        {
            MessageBox.Show(this, "Endpoint, deployment, and API key are required.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _secretManager.Save(new AzureOpenAiSecrets
            {
                Endpoint = endpoint,
                ApiKey = apiKey,
                Deployment = deployment,
            });
            _apiKeyPasswordBox.Clear();
            DialogResult = true;
        }
        catch
        {
            _apiKeyPasswordBox.Clear();
            MessageBox.Show(this,
                "Credentials could not be saved. Verify that this Windows account can write the application configuration directory.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
