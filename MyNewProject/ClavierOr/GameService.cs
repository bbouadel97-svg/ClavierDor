using ClavierOr.Models;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.IO;

namespace ClavierOr;

public class GameService
{
    private List<Role>? _rolesCache;

    public void InitialiserDonnees()
    {
        using var db = new ClavierOrContext();

        if (!db.Set<Role>().Any(r => r.Nom == "Front-End"))
        {
            db.Set<Role>().Add(new Role("Front-End", "Spécialiste interface", 3, 1.0, "Peut changer de question + bonus de fluidité UI"));
        }

        if (!db.Set<Role>().Any(r => r.Nom == "Back-End"))
        {
            db.Set<Role>().Add(new Role("Back-End", "Spécialiste logique serveur", 5, 1.0, "Rattrapage automatique une fois + robustesse"));
        }

        if (!db.Set<Role>().Any(r => r.Nom == "Mobile"))
        {
            db.Set<Role>().Add(new Role("Mobile", "Spécialiste mobile", 2, 1.05, "Accès aux indices + multiplicateur léger"));
        }

        ReinitialiserQuestionsDepuisCsv(db);

        db.SaveChanges();
        _rolesCache = null; // Invalidate cache
    }

    private static void ReinitialiserQuestionsDepuisCsv(ClavierOrContext db)
    {
        // Source unique: QUESTIONS.CSV. On vide puis on recharge pour éviter les décalages de catégories.
        // ⚠️ Important: Supprimer les réponses d'abord (FK constraint)
        var reponses = db.Reponses.ToList();
        if (reponses.Any())
        {
            db.Reponses.RemoveRange(reponses);
            db.SaveChanges(); // Flush pour respecter la contrainte FK
        }

        var questions = db.Questions.ToList();
        if (questions.Any())
        {
            db.Questions.RemoveRange(questions);
            db.SaveChanges();
        }

        ImporterQuestionsDepuisCsv(db);
    }

    private static void ImporterQuestionsDepuisCsv(ClavierOrContext db)
    {
        var csvPath = TrouverQuestionsCsvPath();
        if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
        {
            return;
        }

        var rows = File.ReadLines(csvPath)
            .Select(ParserLigneQuestionCsv)
            .Where(parsed => parsed is not null)
            .Select(parsed => parsed!.Value)
            .ToList();

        if (rows.Count == 0)
        {
            return;
        }

        var rowsParTheme = rows
            .GroupBy(r => r.Theme)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var row in rows)
        {
            var enonceAvecTheme = $"[Thème {row.Theme}] {row.Enonce}";
            var mauvaisesReponses = ConstruireMauvaisesReponses(rowsParTheme, row, 3);

            var reponses = new List<Reponse>
            {
                new(row.Reponse, true)
            };

            reponses.AddRange(mauvaisesReponses.Select(texte => new Reponse(texte, false)));

            AjouterQuestionEtReponsesManquantes(
                db,
                enonceAvecTheme,
                DifficulteQuestion.Facile,
                MapperThemeVersCategorie(row.Theme),
                reponses.ToArray());
        }
    }

    private static List<string> ConstruireMauvaisesReponses(
        Dictionary<int, List<(int Theme, string Enonce, string Reponse)>> rowsParTheme,
        (int Theme, string Enonce, string Reponse) row,
        int maxCount)
    {
        if (!rowsParTheme.TryGetValue(row.Theme, out var themeRows) || themeRows.Count <= 1)
        {
            return new List<string>();
        }

        var candidates = themeRows
            .Where(r => !string.Equals(r.Enonce, row.Enonce, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Reponse.Trim())
            .Where(r => !string.IsNullOrWhiteSpace(r) && !string.Equals(r, row.Reponse, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
        {
            return new List<string>();
        }

        var startIndex = Math.Abs(row.Enonce.GetHashCode()) % candidates.Count;

        return candidates
            .Skip(startIndex)
            .Concat(candidates.Take(startIndex))
            .Take(maxCount)
            .ToList();
    }

    private static (int Theme, string Enonce, string Reponse)? ParserLigneQuestionCsv(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var trimmed = line.Trim();
        var firstCommaIndex = trimmed.IndexOf(',');
        if (firstCommaIndex <= 0)
        {
            return null;
        }

        // ⚡ OPTIMIZATION: Éviter Trim() répétés + allocations
        var themeText = trimmed[..firstCommaIndex].Trim();
        if (!int.TryParse(themeText, out var theme))
        {
            return null;
        }

        var rest = trimmed[(firstCommaIndex + 1)..].TrimStart();
        if (string.IsNullOrWhiteSpace(rest))
        {
            return null;
        }

        string enonce;
        string reponse;

        // ⚡ OPTIMIZATION: Chercher '?' et ',' en une seule passe
        var questionMarkIndex = rest.IndexOf('?');
        if (questionMarkIndex >= 0)
        {
            var commaAfterQuestionIndex = rest.IndexOf(',', questionMarkIndex);
            if (commaAfterQuestionIndex > questionMarkIndex)
            {
                enonce = rest[..(questionMarkIndex + 1)].TrimEnd();
                reponse = rest[(commaAfterQuestionIndex + 1)..].TrimStart();
            }
            else
            {
                // Pas de ',' après '?' → format invalide
                var secondCommaIndex = rest.IndexOf(',');
                if (secondCommaIndex <= 0)
                {
                    return null;
                }
                enonce = rest[..secondCommaIndex].TrimEnd();
                reponse = rest[(secondCommaIndex + 1)..].TrimStart();
            }
        }
        else
        {
            // Pas de '?' → format alternatif
            var secondCommaIndex = rest.IndexOf(',');
            if (secondCommaIndex <= 0)
            {
                return null;
            }
            enonce = rest[..secondCommaIndex].TrimEnd();
            reponse = rest[(secondCommaIndex + 1)..].TrimStart();
        }

        if (string.IsNullOrWhiteSpace(enonce) || string.IsNullOrWhiteSpace(reponse))
        {
            return null;
        }

        return (theme, enonce, reponse);
    }

    private static CategorieQuestion MapperThemeVersCategorie(int theme)
    {
        return theme switch
        {
            1 => CategorieQuestion.Anglais,
            2 => CategorieQuestion.LogiqueRaisonnement,
            3 => CategorieQuestion.AlgorithmiqueProgrammation,
            4 => CategorieQuestion.CultureGenerale,
            5 => CategorieQuestion.MetiersInformatique,
            _ => CategorieQuestion.AlgorithmiqueProgrammation
        };
    }

    private static string? TrouverQuestionsCsvPath()
    {
        // ⚡ OPTIMIZATION: Pas de caching car c'est une fonction statique
        // (les caches statiques dans les méthodes statiques causent des problèmes)
        // Pour cacher, il faudrait faire une instance de GameService
        var possiblePaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "QUESTIONS.CSV"),
            Path.Combine(Directory.GetCurrentDirectory(), "QUESTIONS.CSV"),
            Path.Combine(Directory.GetCurrentDirectory(), "MyNewProject", "ClavierOr", "QUESTIONS.CSV")
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var solutionPath = Path.Combine(current.FullName, "MyNewProject.sln");
            if (File.Exists(solutionPath))
            {
                var fromSolution = Path.Combine(current.FullName, "MyNewProject", "ClavierOr", "QUESTIONS.CSV");
                if (File.Exists(fromSolution))
                {
                    return fromSolution;
                }
            }

            current = current.Parent;
        }

        return null;
    }

    private static void AjouterQuestionEtReponsesManquantes(
        ClavierOrContext db,
        string enonce,
        DifficulteQuestion difficulte,
        CategorieQuestion categorie,
        params Reponse[] reponses)
    {
        var question = db.Questions
            .Include(q => q.Reponses)
            .FirstOrDefault(q => q.Enonce == enonce);

        if (question is null)
        {
            question = new Question(enonce, difficulte, categorie);
            foreach (var reponse in reponses)
            {
                question.Reponses.Add(new Reponse(reponse.Texte, reponse.EstCorrect, reponse.Explication));
            }

            db.Questions.Add(question);
            db.SaveChanges(); // ✅ BUGFIX: Sauvegarder les modifications
            return;
        }

        foreach (var reponse in reponses)
        {
            if (question.Reponses.Any(r => r.Texte == reponse.Texte))
            {
                continue;
            }

            question.Reponses.Add(new Reponse(reponse.Texte, reponse.EstCorrect, reponse.Explication));
        }

        db.SaveChanges(); // ✅ BUGFIX: Sauvegarder les modifications après ajout de nouvelles réponses
    }

    public List<Role> GetRoles()
    {
        // ⚡ OPTIMIZATION: Caching pour éviter les requêtes répétées
        if (_rolesCache != null)
        {
            return _rolesCache;
        }

        using var db = new ClavierOrContext();
        _rolesCache = db.Set<Role>().AsNoTracking().OrderBy(r => r.Nom).ToList();
        return _rolesCache;
    }

    public Role? GetRoleById(int? roleId)
    {
        if (roleId is null)
        {
            return null;
        }

        // ⚡ OPTIMIZATION: Utiliser le cache avant requête DB
        var cachedRoles = GetRoles();
        var cached = cachedRoles.FirstOrDefault(r => r.Id == roleId.Value);
        if (cached != null)
        {
            return cached;
        }

        using var db = new ClavierOrContext();
        return db.Set<Role>().AsNoTracking().FirstOrDefault(r => r.Id == roleId.Value);
    }

    public Joueur GetOrCreatePlayer(string pseudo, int roleId)
    {
        using var db = new ClavierOrContext();

        var joueur = db.Joueurs.FirstOrDefault(j => j.Pseudo == pseudo);
        if (joueur is null)
        {
            joueur = new Joueur(pseudo, $"{pseudo.ToLowerInvariant()}@clavieror.local")
            {
                RoleId = roleId
            };
            db.Joueurs.Add(joueur);
        }
        else
        {
            joueur.RoleId = roleId;
        }

        db.SaveChanges();
        return joueur;
    }

    public Joueur? FindPlayerByPseudo(string pseudo)
    {
        using var db = new ClavierOrContext();
        // ⚡ OPTIMIZATION: AsNoTracking pour lecture simple
        return db.Joueurs.AsNoTracking().FirstOrDefault(j => j.Pseudo == pseudo);
    }

    public Partie StartNewPartie(int joueurId)
    {
        using var db = new ClavierOrContext();

        var partie = new Partie(ModeJeu.NouvellePartie)
        {
            Etat = EtatPartie.EnCours,
            DateDebut = DateTime.Now,
            QuestionActuelleIndex = 0
        };

        var joueur = db.Joueurs.First(j => j.Id == joueurId);
        partie.Joueurs.Add(joueur);

        db.Parties.Add(partie);
        db.SaveChanges();

        LogAction(joueurId, TypeAction.NouvellePartie, "Nouvelle partie démarrée", partie.Id);
        return partie;
    }

    public Partie ResumeOrCreatePartie(int joueurId)
    {
        using var db = new ClavierOrContext();
        var partie = db.Parties
            .Include(p => p.Joueurs)
            .Where(p => p.Etat == EtatPartie.EnCours || p.Etat == EtatPartie.EnPause)
            .OrderByDescending(p => p.DateDebut)
            .FirstOrDefault(p => p.Joueurs.Any(j => j.Id == joueurId));

        if (partie is null)
        {
            return StartNewPartie(joueurId);
        }

        if (partie.Etat == EtatPartie.EnPause)
        {
            partie.Reprendre();
            db.SaveChanges();
        }

        return partie;
    }

    public Score CreateScore(int joueurId, int partieId)
    {
        using var db = new ClavierOrContext();

        var score = new Score(joueurId, partieId)
        {
            DatePartie = DateTime.Now,
            TempsTotal = TimeSpan.Zero
        };

        db.Scores.Add(score);
        db.SaveChanges();

        return score;
    }

    public Score ResumeOrCreateScore(int joueurId, int partieId)
    {
        using var db = new ClavierOrContext();
        var score = db.Scores
            .OrderByDescending(s => s.DatePartie)
            .FirstOrDefault(s => s.JoueurId == joueurId && s.PartieId == partieId);

        return score ?? CreateScore(joueurId, partieId);
    }

    public Question? GetCurrentQuestion(int partieId, CategorieQuestion? categorie = null)
    {
        using var db = new ClavierOrContext();

        var partie = db.Parties.AsNoTracking().FirstOrDefault(p => p.Id == partieId);

        if (partie is null)
        {
            return null;
        }

        var questionsQuery = FiltrerParCategorie(db.Questions.AsNoTracking(), categorie);
        var totalQuestions = questionsQuery.Count();
        if (totalQuestions == 0)
        {
            return null;
        }

        if (partie.QuestionActuelleIndex < 0 || partie.QuestionActuelleIndex >= totalQuestions)
        {
            return null;
        }

        return questionsQuery
            .OrderBy(q => q.Id)
            .Skip(partie.QuestionActuelleIndex)
            .FirstOrDefault();
    }

    public int GetQuestionIndex(int partieId)
    {
        using var db = new ClavierOrContext();
        return db.Parties.AsNoTracking().Where(p => p.Id == partieId).Select(p => p.QuestionActuelleIndex).FirstOrDefault();
    }

    public int GetQuestionCount(int partieId, CategorieQuestion? categorie = null)
    {
        using var db = new ClavierOrContext();
        return FiltrerParCategorie(db.Questions.AsNoTracking(), categorie).Count();
    }

    public List<Reponse> GetReponsesForQuestion(int questionId)
    {
        using var db = new ClavierOrContext();
        return db.Reponses.AsNoTracking().Where(r => r.QuestionId == questionId).OrderBy(r => r.Id).ToList();
    }

    public bool MoveNextQuestion(int partieId, CategorieQuestion? categorie = null)
    {
        using var db = new ClavierOrContext();
        var partie = db.Parties.FirstOrDefault(p => p.Id == partieId);
        if (partie is null)
        {
            return false;
        }

        var questionCount = FiltrerParCategorie(db.Questions.AsQueryable(), categorie).Count();
        if (partie.QuestionActuelleIndex + 1 >= questionCount)
        {
            return false;
        }

        partie.QuestionActuelleIndex++;
        db.SaveChanges();
        return true;
    }

    private static IQueryable<Question> FiltrerParCategorie(IQueryable<Question> query, CategorieQuestion? categorie)
    {
        if (categorie is null)
        {
            return query;
        }

        return query.Where(q => q.Categorie == categorie.Value);
    }

    public void PersistProgress(Joueur joueur, Partie partie, Score score, bool logAction = true)
    {
        using var db = new ClavierOrContext();

        var dbJoueur = db.Joueurs.FirstOrDefault(j => j.Id == joueur.Id);
        if (dbJoueur is not null)
        {
            dbJoueur.Pseudo = joueur.Pseudo;
            dbJoueur.Email = joueur.Email;
            dbJoueur.DateInscription = joueur.DateInscription;
            dbJoueur.ScoreTotal = joueur.ScoreTotal;
            dbJoueur.NiveauActuel = joueur.NiveauActuel;
            dbJoueur.ExperienceActuelle = joueur.ExperienceActuelle;
            dbJoueur.EstClavierOr = joueur.EstClavierOr;
            dbJoueur.RoleId = joueur.RoleId;
            dbJoueur.BossVaincus = joueur.BossVaincus;
            dbJoueur.DateDernierBoss = joueur.DateDernierBoss;
        }

        var dbPartie = db.Parties.FirstOrDefault(p => p.Id == partie.Id);
        if (dbPartie is not null)
        {
            dbPartie.DateDebut = partie.DateDebut;
            dbPartie.DateFin = partie.DateFin;
            dbPartie.Etat = partie.Etat;
            dbPartie.Mode = partie.Mode;
                dbPartie.QuestionActuelleIndex = Math.Max(dbPartie.QuestionActuelleIndex, partie.QuestionActuelleIndex);
                partie.QuestionActuelleIndex = dbPartie.QuestionActuelleIndex;
            dbPartie.StreakActuelle = partie.StreakActuelle;
            dbPartie.MeilleurStreak = partie.MeilleurStreak;
        }

        var dbScore = db.Scores.FirstOrDefault(s => s.Id == score.Id);
        if (dbScore is not null)
        {
            dbScore.Points = score.Points;
            dbScore.BonnesReponses = score.BonnesReponses;
            dbScore.MauvaisesReponses = score.MauvaisesReponses;
            dbScore.TempsTotal = score.TempsTotal;
            dbScore.DatePartie = score.DatePartie;
            dbScore.JokersUtilises = score.JokersUtilises;
            dbScore.PourcentageReussite = score.PourcentageReussite;
            dbScore.StreakMaximum = score.StreakMaximum;
        }

        db.SaveChanges();

        if (logAction)
        {
            LogAction(joueur.Id, TypeAction.ScoreEnregistre, "Progression enregistrée", partie.Id);
        }
    }

    public void FinishPartie(Joueur joueur, Partie partie, Score score)
    {
        using var db = new ClavierOrContext();

        var dbPartie = db.Parties.AsNoTracking().FirstOrDefault(p => p.Id == partie.Id);
        if (dbPartie is null || dbPartie.Etat == EtatPartie.Terminee || dbPartie.Etat == EtatPartie.Abandonnee)
        {
            partie.Etat = dbPartie?.Etat ?? partie.Etat;
            return;
        }

        partie.Terminer();
        score.DatePartie = DateTime.Now;
        score.CalculerPourcentage();

        joueur.AjouterScore(score);
        if (score.PourcentageReussite >= 70)
        {
            joueur.VaincreBoss();
            LogAction(joueur.Id, TypeAction.BossVaincu, "Boss battu grâce à une bonne réussite.", partie.Id);
        }

        if (joueur.EstClavierOr)
        {
            LogAction(joueur.Id, TypeAction.ClavierOrObtenu, "Titre Clavier d'Or atteint !", partie.Id);
        }

        PersistProgress(joueur, partie, score, logAction: false);

        LogAction(joueur.Id, TypeAction.PartieTerminee, $"Partie terminée avec {score.Points} points", partie.Id);
    }

    public void LogAction(int joueurId, TypeAction action, string description, int? partieId = null)
    {
        using var db = new ClavierOrContext();
        db.Historiques.Add(new Historique(joueurId, action, description)
        {
            PartieId = partieId,
            DateAction = DateTime.Now
        });
        db.SaveChanges();
    }

    public List<Score> GetScoresWithPlayers()
    {
        using var db = new ClavierOrContext();
        // ⚡ OPTIMIZATION: AsNoTracking pour statistiques/lecture seule
        return db.Scores
            .Include(s => s.Joueur)
            .AsNoTracking()
            .OrderByDescending(s => s.DatePartie)
            .Take(50)
            .ToList();
    }

    public string GenerateHint(int questionId)
    {
        using var db = new ClavierOrContext();
        // ⚡ OPTIMIZATION: AsNoTracking pour lecture simple
        var question = db.Questions.AsNoTracking().FirstOrDefault(q => q.Id == questionId);
        if (question is null)
        {
            return "Question introuvable.";
        }

        var correct = db.Reponses.AsNoTracking().FirstOrDefault(r => r.QuestionId == questionId && r.EstCorrect);
        if (correct is null)
        {
            return "Aucun indice disponible.";
        }

        if (correct.Texte.Length <= 3)
        {
            return $"La réponse commence par: {correct.Texte}";
        }

        return $"La réponse commence par: {correct.Texte[..3]}...";
    }

    public string ExportLastScorePdf(int joueurId, string outputPath)
    {
        using var db = new ClavierOrContext();
        var score = db.Scores
            .Include(s => s.Joueur)
            .Include(s => s.Partie)
            .Where(s => s.JoueurId == joueurId)
            .OrderByDescending(s => s.DatePartie)
            .FirstOrDefault();

        if (score is null)
        {
            throw new InvalidOperationException("Aucun score à exporter.");
        }

        var roleName = db.Set<Role>()
            .Where(r => r.Id == score.Joueur!.RoleId)
            .Select(r => r.Nom)
            .FirstOrDefault() ?? "-";

        QuestPDF.Settings.License = LicenseType.Community;

        Document
            .Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(32);
                    page.DefaultTextStyle(x => x.FontSize(11));

                    page.Content().Column(column =>
                    {
                        column.Spacing(8);

                        column.Item()
                            .Text("Clavier d'Or - Rapport de score")
                            .Bold()
                            .FontSize(20)
                            .FontColor(Colors.Blue.Darken2);

                        column.Item().Text($"Joueur: {score.Joueur!.Pseudo}").Bold();
                        column.Item().Text($"Rôle: {roleName}");
                        column.Item().Text($"Date: {score.DatePartie:dd/MM/yyyy HH:mm}");

                        column.Item().PaddingVertical(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);

                        column.Item().Text($"Points: {score.Points}").Bold().FontSize(14);
                        column.Item().Text($"Bonnes réponses: {score.BonnesReponses}");
                        column.Item().Text($"Mauvaises réponses: {score.MauvaisesReponses}");
                        column.Item().Text($"Réussite: {score.PourcentageReussite:F1}%");
                        column.Item().Text($"Streak max: {score.StreakMaximum}");

                        column.Item().PaddingTop(20).Text("Export généré automatiquement par Clavier d'Or.").Italic().FontSize(9).FontColor(Colors.Grey.Darken1);
                    });
                });
            })
            .GeneratePdf(outputPath);

        return outputPath;
    }
}
