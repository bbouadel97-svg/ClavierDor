# Clavier d'Or

Application de quiz compétitif en C# / WPF avec persistance SQLite via Entity Framework Core.

Fonctionnalité incluse: progression par étape (`Réussite`, `Affrontement des boss`, `Clavier d'Or`) affichée en temps réel dans le tableau de bord.

## Contenu de la solution

- `MyNewProject` : petit projet console de démonstration.
- `ClavierOr` : application WPF principale (jeu, rôles, scores, historique, export PDF).

## Prérequis

- .NET SDK 10.0+
- Windows (WPF)

## Installation

Depuis la racine du dépôt :

```powershell
dotnet restore MyNewProject.sln
```

## Lancer l'application ClavierOr

```powershell
dotnet run --project MyNewProject/ClavierOr/ClavierOr.csproj
```

## Build de la solution

```powershell
dotnet build MyNewProject.sln
```

## Guide d'utilisation rapide

1. Saisir un pseudo joueur.
2. Choisir un rôle :
   - Front-End : peut changer de question.
   - Back-End : bénéficie d'un rattrapage automatique (1 fois).
   - Mobile : peut demander un indice.
3. Cliquer sur Nouvelle partie.
4. Répondre aux questions et suivre le score en temps réel.
5. Utiliser au besoin : Indice, Changer question, Double points.
6. Cliquer sur Enregistrer pour persister la progression.
7. Cliquer sur Terminer partie pour finaliser le score.
8. Cliquer sur Export PDF pour générer le rapport de score.

## Données et persistance

- Base SQLite locale : `MyNewProject/ClavierOr/clavieror.db`
- Les migrations EF Core sont appliquées au démarrage de l'application.
- Un jeu de rôles et de questions est initialisé automatiquement au premier lancement.

## Vérification manuelle (checklist)

- [ ] Démarrage de l'application sans erreur.
- [ ] Création d'un joueur et lancement d'une nouvelle partie.
- [ ] Reprise d'une partie existante via le pseudo.
- [ ] Mise à jour des scores et de l'historique.
- [ ] Export PDF généré et lisible.
