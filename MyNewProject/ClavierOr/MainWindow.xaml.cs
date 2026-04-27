using ClavierOr.Models;
using Microsoft.Win32;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ClavierOr;

public partial class MainWindow : Window
{
    private const double StackedLayoutWidthThreshold = 1250;

    private sealed record ThemeItem(CategorieQuestion Value, string Label);

    private readonly GameService _gameService = new();
    private Joueur? _currentPlayer;
    private Partie? _currentPartie;
    private Score? _currentScore;
    private Question? _currentQuestion;
    private CategorieQuestion? _currentTheme;
    private bool _doublePointsActive;
    private bool _backEndRattrapageUsed;
    private bool _bossTriggered;
    private bool _bossQuestionActive;

    private DispatcherTimer? _questionTimer;
    private int _timerSeconds;
    private const int TimerDuration = 30;
    private bool _isQuizPageVisible;

    public MainWindow()
    {
        InitializeComponent();
        _gameService.InitialiserDonnees();
        ChargerRoles();
        ChargerThemes();
        ShowHomePage();
        RefreshScoresGrid();
        UpdateUiState();
        ApplyResponsiveLayout();
        KeyDown += MainWindow_KeyDown;
    }

    private void ShowHomePage()
    {
        FiltrerThemesDisponibles();
        AnimatePageTransition(QuizPageGrid, HomePageGrid);
        _isQuizPageVisible = false;
    }

    private void ShowQuizPage()
    {
        var hasActiveGame = _currentPartie is not null
            && _currentScore is not null
            && (_currentPartie.Etat == EtatPartie.EnCours || _currentPartie.Etat == EtatPartie.EnPause);

        if (!hasActiveGame)
        {
            FooterTextBlock.Text = "Lancez ou reprenez une partie depuis l'accueil pour acceder au quiz.";
            return;
        }

        AnimatePageTransition(HomePageGrid, QuizPageGrid);
        _isQuizPageVisible = true;
        ApplyResponsiveLayout();
    }

    private static void AnimatePageTransition(UIElement fromPage, UIElement toPage)
    {
        var fadeOut = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(160)));
        fadeOut.Completed += (_, _) =>
        {
            fromPage.Visibility = Visibility.Collapsed;
            toPage.Opacity = 0;
            toPage.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(220)))
            {
                EasingFunction = new QuadraticEase()
            };
            toPage.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        };

        fromPage.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void BackToHomeButton_Click(object sender, RoutedEventArgs e)
    {
        ShowHomePage();
        FooterTextBlock.Text = "Retour à l'accueil.";
    }

    private void NextThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentPartie is null || _currentScore is null)
        {
            FooterTextBlock.Text = "Aucune partie active pour changer de thème.";
            return;
        }

        StopTimer();
        ShowHomePage();
        FooterTextBlock.Text = "Choisissez un thème puis cliquez 'Reprendre partie'.";
    }

    private bool ProposerNouveauTheme()
    {
        var result = MessageBox.Show(
            "Ce thème est terminé. Voulez-vous continuer la même partie sur un nouveau thème ?",
            "Thème terminé",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
        {
            QuestionTextBlock.Text = "Thème terminé. Cliquez sur 'Terminer partie' ou changez de thème depuis l'accueil.";
            AnswersPanel.Children.Clear();
            StatusTextBlock.Text = "Fin du thème";
            return false;
        }

        StopTimer();
        ShowHomePage();
        FooterTextBlock.Text = "Choisissez un nouveau thème puis cliquez 'Reprendre partie'.";
        return true;
    }

    private void GererFinDeTheme()
    {
        // Marquer le thème comme terminé → bloque tout futur accès même dans la même partie
        if (_currentPlayer is not null && _currentTheme is not null)
        {
            _gameService.MarkThemeAsCompleted(_currentPlayer.Id, _currentTheme.Value);
        }

        var points = _currentScore?.Points ?? 0;
        var pourcentage = _currentScore?.PourcentageReussite ?? 0;
        var bonnes = _currentScore?.BonnesReponses ?? 0;
        var mauvaises = _currentScore?.MauvaisesReponses ?? 0;

        MessageBox.Show(
            $"🎉 Bravo, vous avez terminé le thème \"{_currentTheme}\" !\n\n" +
            $"✅ Bonnes réponses : {bonnes}\n" +
            $"❌ Mauvaises réponses : {mauvaises}\n" +
            $"⭐ Score : {points} pts\n" +
            $"📊 Taux de réussite : {pourcentage:0.#} %",
            "Thème terminé — Bien joué !",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        if (!ProposerNouveauTheme())
        {
            UpdateUiState();
            return;
        }

        UpdateUiState();
    }

    private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (AnswersPanel.Children.Count == 0) return;
        int index = e.Key switch
        {
            System.Windows.Input.Key.A or System.Windows.Input.Key.D1 or System.Windows.Input.Key.NumPad1 => 0,
            System.Windows.Input.Key.B or System.Windows.Input.Key.D2 or System.Windows.Input.Key.NumPad2 => 1,
            System.Windows.Input.Key.C or System.Windows.Input.Key.D3 or System.Windows.Input.Key.NumPad3 => 2,
            System.Windows.Input.Key.D or System.Windows.Input.Key.D4 or System.Windows.Input.Key.NumPad4 => 3,
            _ => -1
        };
        if (index >= 0 && index < AnswersPanel.Children.Count)
        {
            var btn = (Button)AnswersPanel.Children[index];
            btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout();
    }

    private void ApplyResponsiveLayout()
    {
        if (!_isQuizPageVisible)
        {
            return;
        }

        var useStackedLayout = ActualWidth < StackedLayoutWidthThreshold;
        var answersColumns = ActualWidth >= 1600 ? 4 : ActualWidth >= 1150 ? 3 : ActualWidth >= 850 ? 2 : 1;

        MainContentGrid.RowDefinitions.Clear();
        MainContentGrid.ColumnDefinitions.Clear();
        AnswersPanel.Columns = answersColumns;

        if (useStackedLayout)
        {
            MainContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            MainContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            MainContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetColumn(QuestionPanelBorder, 0);
            Grid.SetRow(QuestionPanelBorder, 0);
            QuestionPanelBorder.Margin = new Thickness(0, 0, 0, 10);

            Grid.SetColumn(DashboardPanelBorder, 0);
            Grid.SetRow(DashboardPanelBorder, 1);
            DashboardPanelBorder.Margin = new Thickness(0);
            return;
        }

        MainContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        MainContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.2, GridUnitType.Star) });
        MainContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });

        Grid.SetColumn(QuestionPanelBorder, 0);
        Grid.SetRow(QuestionPanelBorder, 0);
        QuestionPanelBorder.Margin = new Thickness(0, 0, 10, 0);

        Grid.SetColumn(DashboardPanelBorder, 1);
        Grid.SetRow(DashboardPanelBorder, 0);
        DashboardPanelBorder.Margin = new Thickness(0);
    }

    private void ChargerRoles()
    {
        RoleComboBox.ItemsSource = _gameService.GetRoles();
        RoleComboBox.DisplayMemberPath = nameof(Role.Nom);
        RoleComboBox.SelectedValuePath = nameof(Role.Id);
        RoleComboBox.SelectedIndex = 0;
    }

    private static readonly ThemeItem[] TousLesThemes =
    [
        new ThemeItem(CategorieQuestion.Anglais, "Anglais (vocabulaire et traduction)"),
        new ThemeItem(CategorieQuestion.LogiqueRaisonnement, "Logique et raisonnement"),
        new ThemeItem(CategorieQuestion.AlgorithmiqueProgrammation, "Algorithmique et programmation"),
        new ThemeItem(CategorieQuestion.CultureGenerale, "Culture generale"),
        new ThemeItem(CategorieQuestion.MetiersInformatique, "Metiers de l'informatique")
    ];

    private void ChargerThemes()
    {
        ThemeComboBox.ItemsSource = TousLesThemes;
        ThemeComboBox.DisplayMemberPath = nameof(ThemeItem.Label);
        ThemeComboBox.SelectedValuePath = nameof(ThemeItem.Value);
        ThemeComboBox.SelectedIndex = -1;
    }

    private void FiltrerThemesDisponibles()
    {
        var termines = _currentPlayer is not null
            ? _gameService.GetThemesTermines(_currentPlayer.Id)
            : new System.Collections.Generic.HashSet<CategorieQuestion>();

        var disponibles = TousLesThemes
            .Where(t => !termines.Contains(t.Value))
            .ToArray();

        var selected = ThemeComboBox.SelectedValue as CategorieQuestion?;
        ThemeComboBox.ItemsSource = disponibles;

        // Garder la sélection si le thème est encore disponible
        if (selected.HasValue && disponibles.Any(t => t.Value == selected.Value))
            ThemeComboBox.SelectedValue = selected.Value;
        else
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

        if (_gameService.IsThemeAlreadyPlayedByPlayer(_currentPlayer.Id, theme))
        {
            MessageBox.Show("Ce thème a déjà été joué par ce joueur. Choisissez un autre thème.");
            FooterTextBlock.Text = "Thème déjà joué pour ce joueur.";
            return;
        }

        _currentPartie = _gameService.StartNewPartie(_currentPlayer.Id);
        _currentScore = _gameService.CreateScore(_currentPlayer.Id, _currentPartie.Id);
        _gameService.RegisterThemeForPartie(_currentPlayer.Id, _currentPartie.Id, theme);
        _doublePointsActive = false;
        _backEndRattrapageUsed = false;
        _bossTriggered = false;
        _bossQuestionActive = false;

        AjouterHistoriqueLocal($"Nouvelle partie pour {_currentPlayer.Pseudo} - thème {_currentTheme}.");
        ChargerQuestionActuelle();
        ShowQuizPage();
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

        if (_gameService.IsThemeAlreadyPlayedByPlayer(_currentPlayer.Id, theme, _currentPartie.Id))
        {
            MessageBox.Show("Ce thème a déjà été joué par ce joueur. Choisissez un autre thème.");
            FooterTextBlock.Text = "Thème déjà joué pour ce joueur.";
            ShowHomePage();
            UpdateUiState();
            return;
        }

        var themeEstNouveau = _gameService.RegisterThemeForPartie(_currentPlayer.Id, _currentPartie.Id, theme);
        _currentScore = _gameService.ResumeOrCreateScore(player.Id, _currentPartie.Id);
        _currentQuestion = _gameService.GetCurrentQuestion(_currentPartie.Id, _currentTheme);

        if (_currentQuestion is null)
        {
            // Si le thème était déjà enregistré pour cette partie et qu'il n'y a plus de questions
            // → le thème a été terminé, on bloque l'accès (couvre aussi les anciennes données sans ThemeTermine)
            if (!themeEstNouveau)
            {
                MessageBox.Show("Ce thème a déjà été joué par ce joueur. Choisissez un autre thème.");
                FooterTextBlock.Text = "Thème déjà joué pour ce joueur.";
                ShowHomePage();
                UpdateUiState();
                return;
            }

            // Nouveau thème sélectionné : l'index vient d'un thème précédent, on repart à 0
            _gameService.SetQuestionIndex(_currentPartie.Id, 0);
            _currentPartie.QuestionActuelleIndex = 0;
            _currentQuestion = _gameService.GetCurrentQuestion(_currentPartie.Id, _currentTheme);
            if (_currentQuestion is null)
            {
                MessageBox.Show("Aucune question disponible pour ce thème.");
                ShowHomePage();
                FooterTextBlock.Text = "Choisissez un autre thème puis cliquez 'Reprendre partie'.";
                UpdateUiState();
                return;
            }
        }

        AfficherQuestion(_currentQuestion);

        AjouterHistoriqueLocal($"Partie reprise - thème {_currentTheme}.");
        ShowQuizPage();
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

        StopTimer();
        _gameService.FinishPartie(_currentPlayer, _currentPartie, _currentScore);
        AjouterHistoriqueLocal("Partie terminée.");
        RefreshScoresGrid();
        MessageBox.Show($"Partie terminée. Score final: {_currentScore.Points}");
        _currentPartie.Etat = EtatPartie.Terminee;
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
            GererFinDeTheme();
            return;
        }

        AfficherQuestion(_currentQuestion);
    }

    private void StartQuestionTimer()
    {
        _questionTimer?.Stop();
        _timerSeconds = TimerDuration;
        TimerBar.Value = TimerDuration;
        TimerText.Text = _timerSeconds.ToString();
        TimerBarForeground.Color = Color.FromRgb(0x3D, 0xAA, 0x72);

        _questionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _questionTimer.Tick += (s, e) =>
        {
            _timerSeconds--;
            TimerBar.Value = _timerSeconds;
            TimerText.Text = _timerSeconds.ToString();

            // Couleur vire au rouge vif quand il reste peu de temps
            if (_timerSeconds <= 10)
                TimerBarForeground.Color = Color.FromRgb(0xFF, 0x8C, 0x00); // orange d'alerte

            if (_timerSeconds <= 0)
            {
                _questionTimer.Stop();
                // Temps écoulé : compte comme mauvaise réponse et passe
                if (_currentScore is not null)
                {
                    _currentScore.MauvaisesReponses++;
                    _currentPartie?.ReinitialiserStreak();
                    _currentScore.CalculerPourcentage();
                }
                HintTextBlock.Text = "Temps écoulé !";
                UpdateUiState();
                if (_bossQuestionActive)
                {
                    _bossQuestionActive = false;
                    RetirerStyleBoss();
                    HintTextBlock.Text = "Temps écoulé ! Le boss résiste... continuez à progresser.";
                }
                if (_currentPartie is not null && _gameService.MoveNextQuestion(_currentPartie.Id, _currentTheme))
                    ChargerQuestionActuelle();
                else
                {
                    GererFinDeTheme();
                }
            }
        };
        _questionTimer.Start();
    }

    private void LancerQuestionBoss()
    {
        var bossQuestion = _gameService.GetBossQuestion(_currentTheme);
        if (bossQuestion is null)
        {
            // Pas de question disponible, on avance normalement
            var hasNext = _gameService.MoveNextQuestion(_currentPartie!.Id, _currentTheme);
            if (hasNext)
                ChargerQuestionActuelle();
            else
            {
                GererFinDeTheme();
            }
            return;
        }

        _bossQuestionActive = true;
        _currentQuestion = bossQuestion;
        AppliquerStyleBoss();
        AfficherQuestion(bossQuestion);
        // Surcharger le statut après AfficherQuestion pour afficher le message boss
        StatusTextBlock.Text = "⚔ QUESTION BOSS — Réponds correctement pour vaincre le boss !";
        AjouterHistoriqueLocal("⚔ Question Boss déclenchée !");
        FooterTextBlock.Text = "⚔ BOSS — Réponds correctement pour vaincre le boss et gagner +100 pts bonus !";
    }

    private void AppliquerStyleBoss()
    {
        QuestionHighlightBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xD7, 0x00));
        QuestionHighlightBorder.Background = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x50, 0x00));
        QuestionHighlightBorder.BorderThickness = new Thickness(3);
    }

    private void RetirerStyleBoss()
    {
        QuestionHighlightBorder.BorderBrush = (Brush)FindResource("QuestionHighlightBorderBrush");
        QuestionHighlightBorder.Background = (Brush)FindResource("QuestionHighlightBackgroundBrush");
        QuestionHighlightBorder.BorderThickness = new Thickness(2);
    }

    private void StopTimer()
    {
        _questionTimer?.Stop();
        TimerText.Text = "--";
        TimerBar.Value = 0;
    }

    private void FlashQuestionBorder(bool correct)
    {
        var targetColor = correct
            ? Color.FromArgb(0x55, 0x22, 0xBB, 0x66)
            : Color.FromArgb(0x55, 0xCC, 0x88, 0x00); // orange pour erreur, plus doux qu'un rouge vif
        var original = Color.FromArgb(0x1A, 0x0E, 0x40, 0x22);

        var anim = new ColorAnimation(targetColor, original, new Duration(TimeSpan.FromMilliseconds(600)))
        {
            EasingFunction = new QuadraticEase()
        };
        var brush = new SolidColorBrush(original);
        QuestionHighlightBorder.Background = brush;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }

    private void AnimateQuestionFadeIn()
    {
        QuestionHighlightBorder.Opacity = 0;
        var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(220)));
        QuestionHighlightBorder.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    private void UpdateStreakBadge()
    {
        var streak = _currentPartie?.StreakActuelle ?? 0;
        if (streak >= 2)
        {
            StreakBadge.Visibility = Visibility.Visible;
            StreakText.Text = $"\U0001F525 x{streak}";
            var pulse = new DoubleAnimation(1.15, 1.0, new Duration(TimeSpan.FromMilliseconds(300)))
            {
                EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 5 }
            };
            StreakBadge.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            StreakBadge.RenderTransform = new ScaleTransform();
            ((ScaleTransform)StreakBadge.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
            ((ScaleTransform)StreakBadge.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        }
        else
        {
            StreakBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void AfficherQuestion(Question question)
    {
        QuestionTextBlock.Text = question.Enonce;
        var total = _gameService.GetQuestionCount(_currentPartie!.Id, _currentTheme);
        StatusTextBlock.Text = $"Thème: {_currentTheme} | Question {_gameService.GetQuestionIndex(_currentPartie.Id) + 1}/{total} | Difficulté: {question.Difficulte}";
        HintTextBlock.Text = string.Empty;

        //BUGFIX: Nettoyer les anciens event handlers pour éviter memory leak
        foreach (Button oldButton in AnswersPanel.Children.OfType<Button>())
        {
            oldButton.Click -= AnswerButton_Click;
        }
        AnswersPanel.Children.Clear();

        var  reponsesMelangees = _gameService
            .GetReponsesForQuestion(question.Id)
            .OrderBy(_ => Random.Shared.Next())
            .ToList();

        AnimateQuestionFadeIn();
        StartQuestionTimer();

        for (var index = 0; index < reponsesMelangees.Count; index++)
        {
            var reponse = reponsesMelangees[index];
            var choix = (char)('A' + (index % 26));

            var button = new Button
            {
                Content = $"{choix}) {reponse.Texte}",
                Tag = reponse,
                Margin = new Thickness(0, 0, 8, 8),
                Height = 96,
                MinHeight = 0,
                MaxHeight = 120,
                VerticalAlignment = VerticalAlignment.Top,
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

            _questionTimer?.Stop();

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
                FlashQuestionBorder(true);
            }
            else
            {
                _currentScore.MauvaisesReponses++;
                _currentPartie.ReinitialiserStreak();
                HintTextBlock.Text = selected.Explication ?? "Mauvaise réponse.";
                FlashQuestionBorder(false);
            }
            UpdateStreakBadge();

            _currentScore.StreakMaximum = Math.Max(_currentScore.StreakMaximum, _currentPartie.MeilleurStreak);
            _currentScore.CalculerPourcentage();

            // Résolution d'une question boss
            if (_bossQuestionActive)
            {
                _bossQuestionActive = false;
                RetirerStyleBoss();
                if (isCorrect)
                {
                    _currentPlayer.VaincreBoss();
                    _currentScore.Points += 100; // bonus boss
                    HintTextBlock.Text = "🏆 Boss vaincu ! +100 pts bonus. Continuez votre progression !";
                    _gameService.LogAction(_currentPlayer.Id, TypeAction.BossVaincu, "Boss vaincu en cours de partie !", _currentPartie.Id);
                }
                else
                {
                    HintTextBlock.Text = "Le boss résiste... continuez à progresser pour l'affronter à nouveau.";
                }
                _gameService.PersistProgress(_currentPlayer, _currentPartie, _currentScore, logAction: false);
                _gameService.LogAction(_currentPlayer.Id, TypeAction.ScoreEnregistre, "Question boss répondue.", _currentPartie.Id);
                var hasNextAfterBoss = _gameService.MoveNextQuestion(_currentPartie.Id, _currentTheme);
                if (!hasNextAfterBoss)
                {
                    GererFinDeTheme();
                }
                else
                {
                    ChargerQuestionActuelle();
                }
                RefreshScoresGrid();
                UpdateUiState();
                return;
            }

            // Déclenchement de la question boss (première fois ≥ 70% de réussite)
            if (isCorrect && !_bossTriggered && _currentScore.PourcentageReussite >= 70)
            {
                _bossTriggered = true;
                _gameService.PersistProgress(_currentPlayer, _currentPartie, _currentScore, logAction: false);
                _gameService.LogAction(_currentPlayer.Id, TypeAction.ScoreEnregistre, $"Réponse à la question {_currentQuestion.Id}.", _currentPartie.Id);
                LancerQuestionBoss();
                RefreshScoresGrid();
                UpdateUiState();
                return;
            }

            var hasNext = _gameService.MoveNextQuestion(_currentPartie.Id, _currentTheme);

            _gameService.PersistProgress(_currentPlayer, _currentPartie, _currentScore, logAction: false);

            _gameService.LogAction(_currentPlayer.Id, TypeAction.ScoreEnregistre, $"Réponse à la question {_currentQuestion.Id}.", _currentPartie.Id);

            if (!hasNext)
            {
                GererFinDeTheme();
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

        var pendingBrush = Brush("#FFC5AAB0");
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
        var hasActiveGame = hasGame && (_currentPartie!.Etat == EtatPartie.EnCours || _currentPartie.Etat == EtatPartie.EnPause);

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
        HintButton.IsEnabled = hasActiveGame && roleName == "Mobile";
        SkipButton.IsEnabled = hasActiveGame && roleName == "Front-End";
        DoubleButton.IsEnabled = hasActiveGame;
        SaveButton.IsEnabled = hasActiveGame;
        FinishButton.IsEnabled = hasActiveGame;
        ExportPdfButton.IsEnabled = hasGame;
        BackToHomeButton.IsEnabled = true;
        NextThemeButton.IsEnabled = hasActiveGame;
        NextThemeInlineButton.IsEnabled = hasActiveGame;
    }
}
