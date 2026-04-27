namespace ClavierOr.Models;

/// <summary>
/// Représente une entrée dans l'historique des actions du joueur
/// </summary>
public class Historique
{
    public int Id { get; set; }
    public int JoueurId { get; set; }
    public Joueur? Joueur { get; set; }
    
    public DateTime DateAction { get; set; }
    public TypeAction Action { get; set; }
    public string Description { get; set; } = string.Empty;
    public int? PartieId { get; set; }
    public Partie? Partie { get; set; }
    
    public Historique()
    {
        DateAction = DateTime.Now;
    }

    public Historique(int joueurId, TypeAction action, string description) : this()
    {
        JoueurId = joueurId;
        Action = action;
        Description = description;
    }
}

/// <summary>
/// Types d'actions enregistrées dans l'historique
/// </summary>
public enum TypeAction
{
    NouvellePartie,
    ThemeJoue,
    PartieTerminee,
    BossVaincu,
    NiveauGagne,
    ClavierOrObtenu,
    RoleChoisi,
    ScoreEnregistre
}
