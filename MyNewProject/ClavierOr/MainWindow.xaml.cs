using ClavierOr.Models;
using Microsoft.Win32;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClavierOr;

public partial class MainWindow : Window
{
    private sealed record ThemeItem(CategorieQuestion Value, string Label);

    private readonly GameService _gameService = new();
    private Joueur? _currentPlayer;
    private Partie? _currentPartie;
    private Score? _currentScore;
    private Question? _currentQuestion;
    private CategorieQuestion? _currentTheme;
    private bool _doublePointsActive;
    private bool _backEndRattrapageUsed;

    public MainWindow()
    {
        InitializeComponent();
        _gameService.InitialiserDonnees();
        ChargerRoles();
        ChargerThemes();
        RefreshScoresGrid();
        UpdateUiState();
    }

    private void ChargerRoles()
    {
        RoleComboBox.ItemsSource = _gameService.GetRoles();
        RoleComboBox.DisplayMemberPath = nameof(Role.Nom);
        RoleComboBox.SelectedValuePath = nameof(Role.Id);
        RoleComboBox.SelectedIndex = 0;
    }

    private void ChargerThemes()
    {
        ThemeComboBox.ItemsSource = new[]
        {
            new ThemeItem(CategorieQuestion.Anglais, "Anglais (vocabulaire et traduction)"),
            new ThemeItem(CategorieQuestion.LogiqueRaisonnement, "Logique et raisonnement"),
            new ThemeItem(CategorieQuestion.AlgorithmiqueProgrammation, "Algorithmique et programmation"),
            new ThemeItem(CategorieQuestion.CultureGenerale, "Culture generale"),
            new ThemeItem(CategorieQuestion.MetiersInformatique, "Metiers de l'informatique")
        };
        ThemeComboBox.DisplayMemberPath = nameof(ThemeItem.Label);
        ThemeComboBox.SelectedValuePath = nameof(ThemeItem.Value);
        ThemeComboBox.SelectedIndex = -1;
    }

    private void NewGameButton_Click(object sender, RoutedEventArgs e)
    {
        var pseudo = PlayerNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(pseudo))
        {
            MessageBox.Show("Saisissez un nom de joueur.");
            return;
        }

        if (RoleComboBox.SelectedItem is not Role role)
        {
            MessageBox.Show("Sélectionnez un rôle.");
            return;
        }

        if (ThemeComboBox.SelectedValue is not CategorieQuestion theme)
        {
            MessageBox.Show("Sélectionnez un thème avant de démarrer.");
            return;
        }

        _currentTheme = theme;

        _currentPlayer = _gameService.GetOrCreatePlayer(pseudo, role.Id);
        _currentPartie = _gameService.StartNewPartie(_currentPlayer.Id);
        _currentScore = _gameService.CreateScore(_currentPlayer.Id, _currentPartie.Id);
        _doublePointsActive = false;
        _backEndRattrapageUsed = false;

        AjouterHistoriqueLocal($"Nouvelle partie pour {_currentPlayer.Pseudo} - thème {_currentTheme}.");
        ChargerQuestionActuelle();
        UpdateUiState();
        FooterTextBlock.Text = "Partie démarrée.";
    }

    private void ResumeGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PlayerNameTextBox.Text))
        {
            MessageBox.Show("Indiquez le pseudo pour reprendre.");
            return;
        }

        var pseudo = PlayerNameTextBox.Text.Trim();
        var player = _gameService.FindPlayerByPseudo(pseudo);
        if (player is null)
        {
            MessageBox.Show("Aucune partie trouvée pour ce joueur.");
            return;
        }

        if (ThemeComboBox.SelectedValue is not CategorieQuestion theme)
        {
            MessageBox.Show("Sélectionnez un thème avant de reprendre.");
            return;
        }

        _currentTheme = theme;

        _currentPlayer = player;
        _currentPartie = _gameService.ResumeOrCreatePartie(player.Id);
        _currentScore = _gameService.ResumeOrCreateScore(player.Id, _currentPartie.Id);
        _currentQuestion = _gameService.GetCurrentQuestion(_currentPartie.Id, _currentTheme);

        if (_currentQuestion is null)
        {
            ChargerQuestionActuelle();
        }
        else
        {
            AfficherQuestion(_currentQuestion);
        }

        AjouterHistoriqueLocal($"Partie reprise - thème {_currentTheme}.");
        UpdateUiState();
        FooterTextBlock.Text = "Partie reprise.";
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPlayer is null || _currentPartie is null || _currentScore is null)
        {
            MessageBox.Show("Aucune partie active à enregistrer.");
            return;
        }

        _gameService.PersistProgress(_currentPlayer, _currentPartie, _currentScore);
        AjouterHistoriqueLocal("Progression enregistrée.");
        RefreshScoresGrid();
        FooterTextBlock.Text = "Progression enregistrée.";
    }

    private void FinishButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPlayer is null || _currentPartie is null || _currentScore is null)
        {
            MessageBox.Show("Aucune partie en cours.");
            return;
        }

        _gameService.FinishPartie(_currentPlayer, _currentPartie, _currentScore);
        AjouterHistoriqueLocal("Partie terminée.");
        RefreshScoresGrid();
        MessageBox.Show($"Partie terminée. Score final: {_currentScore.Points}");
        FooterTextBlock.Text = "Partie terminée.";
    }

    private void ChargerQuestionActuelle()
    {
        if (_currentPartie is null)
        {
            return;
        }

        if (_currentTheme is null)
        {
            MessageBox.Show("Sélectionnez un thème.");
            return;
        }

        _currentQuestion = _gameService.GetCurrentQuestion(_currentPartie.Id, _currentTheme);
        if (_currentQuestion is null)
        {
            MessageBox.Show("Plus de questions pour ce thème. Terminez la partie.");
            return;
        }

        AfficherQuestion(_currentQuestion);
    }

    private void AfficherQuestion(Question question)
    {
        QuestionTextBlock.Text = question.Enonce;
        var total = _gameService.GetQuestionCount(_currentPartie!.Id, _currentTheme);
        StatusTextBlock.Text = $"Thème: {_currentTheme} | Question {_gameService.GetQuestionIndex(_currentPartie.Id) + 1}/{total} | Difficulté: {question.Difficulte}";
        HintTextBlock.Text = string.Empty;

        AnswersPanel.Children.Clear();
        var reponsesMelangees = _gameService
            .GetReponsesForQuestion(question.Id)
            .OrderBy(_ => Random.Shared.Next())
            .ToList();

        foreach (var reponse in reponsesMelangees)
        {
            var button = new Button
            {
                Content = reponse.Texte,
                Tag = reponse,
                Margin = new Thickness(0, 0, 0, 8),
                Height = 34,
                Style = (Style)FindResource("AnswerButtonStyle")
            };
            button.Click += AnswerButton_Click;
            AnswersPanel.Children.Add(button);
        }
    }

    private void AnswerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_currentPlayer is null || _currentPartie is null || _currentScore is null || _currentQuestion is null)
            {
                return;
            }

            if (sender is not Button { Tag: Reponse selected })
            {
                return;
            }

            var role = _gameService.GetRoleById(_currentPlayer.RoleId);
            var isCorrect = selected.EstCorrect;

            if (!isCorrect && role?.Nom == "Back-End" && !_backEndRattrapageUsed)
            {
                isCorrect = true;
                _backEndRattrapageUsed = true;
                HintTextBlock.Text = "Rattrapage Back-End utilisé: réponse sauvée automatiquement.";
            }

            if (isCorrect)
            {
                var points = _currentQuestion.PointsAttribues;
                if (_doublePointsActive)
                {
                    points *= 2;
                    _doublePointsActive = false;
                }

                if (role is not null)
                {
                    points += role.BonusPoints;
                    points = (int)(points * role.MultiplicateurScore);
                }

                _currentScore.BonnesReponses++;
                _currentScore.Points += points;
                _currentPartie.EnregistrerBonneReponse();
                _currentPlayer.AjouterExperience(15);
                HintTextBlock.Text = "Bonne réponse !";
            }
            else
            {
                _currentScore.MauvaisesReponses++;
                _currentPartie.ReinitialiserStreak();
                HintTextBlock.Text = selected.Explication ?? "Mauvaise réponse.";
            }

            var hasNext = _gameService.MoveNextQuestion(_currentPartie.Id, _currentTheme);
            if (hasNext)
            {
                _currentPartie.QuestionActuelleIndex++;
            }

            _currentScore.StreakMaximum = Math.Max(_currentScore.StreakMaximum, _currentPartie.MeilleurStreak);
            _currentScore.CalculerPourcentage();
            _gameService.PersistProgress(_currentPlayer, _currentPartie, _currentScore, logAction: false);

            _gameService.LogAction(_currentPlayer.Id, TypeAction.ScoreEnregistre, $"Réponse à la question {_currentQuestion.Id}.", _currentPartie.Id);

            if (!hasNext)
            {
                QuestionTextBlock.Text = "Quiz terminé. Cliquez sur 'Terminer partie'.";
                AnswersPanel.Children.Clear();
                StatusTextBlock.Text = "Fin des questions";
            }
            else
            {
                ChargerQuestionActuelle();
            }

            RefreshScoresGrid();
            UpdateUiState();
        }
        catch (Exception ex)
        {
            FooterTextBlock.Text = "Erreur pendant la validation de la réponse.";
            MessageBox.Show($"Une erreur est survenue au clic sur une réponse:\n{ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RoleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RoleComboBox.SelectedItem is Role role)
        {
            RoleInfoTextBlock.Text = $"Rôle: {role.Nom} - {role.AvantageSpecial}";
        }

        UpdateUiState();
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_currentPartie is null)
        {
            if (ThemeComboBox.SelectedValue is CategorieQuestion theme)
            {
                _currentTheme = theme;
            }
            else
            {
                _currentTheme = null;
            }
        }

        UpdateUiState();
    }

    private void HintButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentQuestion is null || _currentPlayer is null)
        {
            return;
        }

        var role = _gameService.GetRoleById(_currentPlayer.RoleId);
        if (role?.Nom != "Mobile")
        {
            HintTextBlock.Text = "L'indice est réservé au rôle Mobile.";
            return;
        }

        var hint = _gameService.GenerateHint(_currentQuestion.Id);
        HintTextBlock.Text = $"Indice: {hint}";
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPlayer is null || _currentPartie is null)
        {
            return;
        }

        var role = _gameService.GetRoleById(_currentPlayer.RoleId);
        if (role?.Nom != "Front-End")
        {
            HintTextBlock.Text = "Le changement de question est réservé au rôle Front-End.";
            return;
        }

        if (_gameService.MoveNextQuestion(_currentPartie.Id, _currentTheme))
        {
            _currentPartie.QuestionActuelleIndex++;
            ChargerQuestionActuelle();
            HintTextBlock.Text = "Question changée grâce au bonus Front-End.";
        }
        else
        {
            HintTextBlock.Text = "Aucune autre question disponible.";
        }
    }

    private void DoubleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPlayer is null)
        {
            return;
        }

        _doublePointsActive = true;
        HintTextBlock.Text = "Double points activé pour la prochaine bonne réponse.";
    }

    private void ExportPdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPlayer is null)
        {
            MessageBox.Show("Démarrez une partie pour exporter votre score.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "Fichier PDF (*.pdf)|*.pdf",
            FileName = $"score_{_currentPlayer.Pseudo}_{DateTime.Now:yyyyMMdd_HHmm}.pdf"
        };

        if (dialog.ShowDialog() == true)
        {
            var savedPath = _gameService.ExportLastScorePdf(_currentPlayer.Id, dialog.FileName);
            MessageBox.Show($"PDF exporté: {savedPath}");
            FooterTextBlock.Text = "PDF exporté avec succès.";
        }
    }

    private void AjouterHistoriqueLocal(string texte)
    {
        HistoryListBox.Items.Insert(0, $"{DateTime.Now:HH:mm:ss} - {texte}");
    }

    private void UpdateProgressionDisplay()
    {
        static SolidColorBrush Brush(string color) => (SolidColorBrush)new BrushConverter().ConvertFromString(color)!;

        var pendingBrush = Brush("#FFB0BDCE");
        var inProgressBrush = Brush("#FFF0A63F");
        var doneBrush = Brush("#FF4AA169");

        if (_currentPlayer is null)
        {
            ReussiteStepDot.Fill = pendingBrush;
            BossStepDot.Fill = pendingBrush;
            ClavierOrStepDot.Fill = pendingBrush;
            ReussiteStepText.Text = "Reussite";
            BossStepText.Text = "Boss";
            ClavierOrStepText.Text = "Clavier d'Or";
            ProgressionInfoTextBlock.Text = "Progression: -";
            return;
        }

        var reussite = _currentScore?.PourcentageReussite ?? 0;
        var etapeReussite = reussite >= 70
            ? "Validee"
            : reussite >= 50
                ? "En cours"
                : "A travailler";

        var bossObjectif = Math.Max(0, 5 - _currentPlayer.BossVaincus);
        var etapeBoss = _currentPlayer.BossVaincus > 0
            ? $"{_currentPlayer.BossVaincus} vaincu(s)"
            : reussite >= 70
                ? "Debloque"
                : "Verrouille";

        var etapeClavierOr = _currentPlayer.EstClavierOr
            ? "Obtenu"
            : $"Niv {_currentPlayer.NiveauActuel}/50 + {bossObjectif} boss";

        ReussiteStepDot.Fill = reussite >= 70 ? doneBrush : reussite >= 50 ? inProgressBrush : pendingBrush;
        BossStepDot.Fill = _currentPlayer.BossVaincus > 0 ? doneBrush : reussite >= 70 ? inProgressBrush : pendingBrush;
        ClavierOrStepDot.Fill = _currentPlayer.EstClavierOr ? doneBrush : (_currentPlayer.NiveauActuel >= 25 || _currentPlayer.BossVaincus >= 2) ? inProgressBrush : pendingBrush;

        ReussiteStepText.Text = $"Reussite ({etapeReussite})";
        BossStepText.Text = $"Boss ({etapeBoss})";
        ClavierOrStepText.Text = _currentPlayer.EstClavierOr ? "Clavier d'Or (Obtenu)" : "Clavier d'Or (En cours)";

        ProgressionInfoTextBlock.Text =
            $"Progression: Reussite [{etapeReussite}] | Boss [{etapeBoss}] | Clavier d'Or [{etapeClavierOr}]";
    }

    private void RefreshScoresGrid()
    {
        ScoresDataGrid.ItemsSource = _gameService.GetScoresWithPlayers();
    }

    private void UpdateUiState()
    {
        var hasGame = _currentPlayer is not null && _currentPartie is not null && _currentScore is not null;

        PlayerInfoTextBlock.Text = _currentPlayer is null
            ? "Joueur: -"
            : $"Joueur: {_currentPlayer.Pseudo} | Niveau: {_currentPlayer.NiveauActuel} | XP: {_currentPlayer.ExperienceActuelle}";

        ScoreInfoTextBlock.Text = _currentScore is null
            ? "Score: -"
            : $"Score: {_currentScore.Points} | Bonnes: {_currentScore.BonnesReponses} | Mauvaises: {_currentScore.MauvaisesReponses} | Réussite: {_currentScore.PourcentageReussite:F0}%";

        UpdateProgressionDisplay();

        if (!hasGame)
        {
            StatusTextBlock.Text = "Prêt";
        }

        var selectedRole = RoleComboBox.SelectedItem as Role;
        var roleName = selectedRole?.Nom ?? string.Empty;
        var hasThemeSelected = ThemeComboBox.SelectedValue is CategorieQuestion;

        NewGameButton.IsEnabled = hasThemeSelected;
        ResumeGameButton.IsEnabled = hasThemeSelected;
        HintButton.IsEnabled = hasGame && roleName == "Mobile";
        SkipButton.IsEnabled = hasGame && roleName == "Front-End";
        DoubleButton.IsEnabled = hasGame;
        SaveButton.IsEnabled = hasGame;
        FinishButton.IsEnabled = hasGame;
        ExportPdfButton.IsEnabled = hasGame;
    }
}
