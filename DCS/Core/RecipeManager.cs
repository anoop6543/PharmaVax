using System;
using System.Collections.Generic;
using System.Linq;

namespace PharmaceuticalProcess.DCS.Core
{
	/// <summary>
	/// Manages ISA-88 compliant recipes for batch processing
	/// </summary>
	public class RecipeManager
	{
		private readonly Dictionary<string, Recipe> _recipes;
		private readonly Dictionary<string, List<string>> _recipeVersions; // RecipeName -> List of versions

		public RecipeManager()
		{
			_recipes = new Dictionary<string, Recipe>();
			_recipeVersions = new Dictionary<string, List<string>>();
		}

		public bool AddRecipe(Recipe recipe)
		{
			if (recipe == null || string.IsNullOrEmpty(recipe.RecipeId))
			{
				return false;
			}

			// Add to recipes
			_recipes[recipe.RecipeId] = recipe;

			// Track versions
			if (!_recipeVersions.ContainsKey(recipe.Name))
			{
				_recipeVersions[recipe.Name] = new List<string>();
			}

			if (!_recipeVersions[recipe.Name].Contains(recipe.Version))
			{
				_recipeVersions[recipe.Name].Add(recipe.Version);
			}

			return true;
		}

		public Recipe GetRecipe(string recipeName, string version = null)
		{
			// If no version specified, get the latest
			if (string.IsNullOrEmpty(version))
			{
				return GetLatestRecipe(recipeName);
			}

			// Find recipe by name and version
			return _recipes.Values.FirstOrDefault(r => r.Name == recipeName && r.Version == version);
		}

		public Recipe GetRecipeById(string recipeId)
		{
			_recipes.TryGetValue(recipeId, out var recipe);
			return recipe;
		}

		public Recipe GetLatestRecipe(string recipeName)
		{
			if (!_recipeVersions.ContainsKey(recipeName))
			{
				return null;
			}

			var latestVersion = _recipeVersions[recipeName].OrderByDescending(v => v).FirstOrDefault();
			return GetRecipe(recipeName, latestVersion);
		}

		public List<string> GetAllRecipeNames()
		{
			return _recipeVersions.Keys.ToList();
		}

		public List<string> GetRecipeVersions(string recipeName)
		{
			if (_recipeVersions.TryGetValue(recipeName, out var versions))
			{
				return new List<string>(versions);
			}

			return new List<string>();
		}

		public bool RemoveRecipe(string recipeId)
		{
			if (_recipes.TryGetValue(recipeId, out var recipe))
			{
				_recipes.Remove(recipeId);

				// Clean up version tracking
				if (_recipeVersions.TryGetValue(recipe.Name, out var versions))
				{
					versions.Remove(recipe.Version);

					if (versions.Count == 0)
					{
						_recipeVersions.Remove(recipe.Name);
					}
				}

				return true;
			}

			return false;
		}

		public Recipe CloneRecipe(string recipeId, string newVersion)
		{
			var originalRecipe = GetRecipeById(recipeId);
			if (originalRecipe == null)
			{
				return null;
			}

			var clonedRecipe = new Recipe
			{
				RecipeId = Guid.NewGuid().ToString(),
				Name = originalRecipe.Name,
				Version = newVersion,
				Description = originalRecipe.Description,
				Phases = ClonePhases(originalRecipe.Phases),
				DefaultParameters = new Dictionary<string, object>(originalRecipe.DefaultParameters)
			};

			AddRecipe(clonedRecipe);
			return clonedRecipe;
		}

		private List<BatchPhase> ClonePhases(List<BatchPhase> phases)
		{
			var clonedPhases = new List<BatchPhase>();

			foreach (var phase in phases)
			{
				var clonedPhase = new BatchPhase
				{
					Name = phase.Name,
					Description = phase.Description,
					Duration = phase.Duration,
					CompletionCriteria = phase.CompletionCriteria,
					Operations = CloneOperations(phase.Operations)
				};

				clonedPhases.Add(clonedPhase);
			}

			return clonedPhases;
		}

		private List<BatchOperation> CloneOperations(List<BatchOperation> operations)
		{
			var clonedOperations = new List<BatchOperation>();

			foreach (var operation in operations)
			{
				var clonedOperation = new BatchOperation
				{
					Name = operation.Name,
					Description = operation.Description,
					Action = operation.Action,
					Parameters = new Dictionary<string, object>(operation.Parameters)
				};

				clonedOperations.Add(clonedOperation);
			}

			return clonedOperations;
		}

		public RecipeValidationResult ValidateRecipe(Recipe recipe)
		{
			var result = new RecipeValidationResult { IsValid = true };

			// Validate basic recipe properties
			if (string.IsNullOrEmpty(recipe.Name))
			{
				result.IsValid = false;
				result.Errors.Add("Recipe name is required");
			}

			if (string.IsNullOrEmpty(recipe.Version))
			{
				result.IsValid = false;
				result.Errors.Add("Recipe version is required");
			}

			if (recipe.Phases == null || recipe.Phases.Count == 0)
			{
				result.IsValid = false;
				result.Errors.Add("Recipe must have at least one phase");
			}

			// Validate phases
			if (recipe.Phases != null)
			{
				for (int i = 0; i < recipe.Phases.Count; i++)
				{
					var phase = recipe.Phases[i];

					if (string.IsNullOrEmpty(phase.Name))
					{
						result.IsValid = false;
						result.Errors.Add($"Phase {i + 1} name is required");
					}

					if (phase.Duration <= 0 && phase.CompletionCriteria == null)
					{
						result.Warnings.Add($"Phase {phase.Name} has no duration or completion criteria");
					}

					if (phase.Operations == null || phase.Operations.Count == 0)
					{
						result.Warnings.Add($"Phase {phase.Name} has no operations");
					}
				}
			}

			return result;
		}
	}

	public class RecipeValidationResult
	{
		public bool IsValid { get; set; }
		public List<string> Errors { get; set; } = new List<string>();
		public List<string> Warnings { get; set; } = new List<string>();
	}
}
