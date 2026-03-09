using System.Collections.ObjectModel;

namespace ClavierOr.Models;

/// <summary>
/// Représente une question du quiz
/// </summary>
public class Question
{
    public int Id { get; set; }
    public string Enonce { get; set; } = string.Empty;
    public DifficulteQuestion Difficulte { get; set; }
    public CategorieQuestion Categorie { get; set; }
    public int PointsAttribues { get; set; }
    public int TempsLimite { get; set; } // En secondes
    
    // Type de question
    public TypeQuestion Type { get; set; }
    
    // Relations
    public ObservableCollection<Reponse> Reponses { get; set; } = new();
    
    public Question()
    {
    }

    public Question(string enonce, DifficulteQuestion difficulte, CategorieQuestion categorie, TypeQuestion type = TypeQuestion.QCM)
    {
        Enonce = enonce;
        Difficulte = difficulte;
        Categorie = categorie;
        Type = type;
        PointsAttribues = CalculerPointsSelonDifficulte(difficulte);
        TempsLimite = CalculerTempsSelonDifficulte(difficulte);
    }

    /// <summary>
    /// Calcule les points selon la difficulté
    /// </summary>
    private int CalculerPointsSelonDifficulte(DifficulteQuestion difficulte)
    {
        return difficulte switch
        {
            DifficulteQuestion.Facile => 10,
            DifficulteQuestion.Moyen => 25,
            DifficulteQuestion.Difficile => 50,
            DifficulteQuestion.Boss => 100,
            _ => 10
        };
    }

    /// <summary>
    /// Calcule le temps limite selon la difficulté
    /// </summary>
    private int CalculerTempsSelonDifficulte(DifficulteQuestion difficulte)
    {
        return difficulte switch
        {
            DifficulteQuestion.Facile => 30,
            DifficulteQuestion.Moyen => 60,
            DifficulteQuestion.Difficile => 90,
            DifficulteQuestion.Boss => 120,
            _ => 30
        };
    }

    /// <summary>
    /// Obtient la ou les bonnes réponses
    /// </summary>
    public IEnumerable<Reponse> ObtenirBonnesReponses()
    {
        return Reponses.Where(r => r.EstCorrect);
    }

    /// <summary>
    /// Vérifie si une réponse est correcte
    /// </summary>
    public bool VerifierReponse(int reponseId)
    {
        var reponse = Reponses.FirstOrDefault(r => r.Id == reponseId);
        return reponse?.EstCorrect ?? false;
    }
}

/// <summary>
/// Niveaux de difficulté des questions
/// </summary>
public enum DifficulteQuestion
{
    Facile,
    Moyen,
    Difficile,
    Boss
}

/// <summary>
/// Catégories de questions
/// </summary>
public enum CategorieQuestion
{
    Anglais,
    LogiqueRaisonnement,
    AlgorithmiqueProgrammation,
    CultureGenerale,
    MetiersInformatique
}

/// <summary>
/// Types de questions
/// </summary>
public enum TypeQuestion
{
    QCM,              // Choix multiples
    VraiFaux,         // Vrai ou Faux
    TexteLibre,       // Réponse textuelle
    Code              // Code à compléter
}
