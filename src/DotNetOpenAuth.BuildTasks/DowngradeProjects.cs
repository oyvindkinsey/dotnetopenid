﻿//-----------------------------------------------------------------------
// <copyright file="DowngradeProjects.cs" company="Andrew Arnott">
//     Copyright (c) Andrew Arnott. All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace DotNetOpenAuth.BuildTasks {
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Text;
	using Microsoft.Build.BuildEngine;
	using Microsoft.Build.Framework;
	using Microsoft.Build.Utilities;

	/// <summary>
	/// Downgrades Visual Studio 2010 solutions and projects so that they load in Visual Studio 2008.
	/// </summary>
	public class DowngradeProjects : Task {
		/// <summary>
		/// Gets or sets the projects and solutions to downgrade.
		/// </summary>
		[Required]
		public ITaskItem[] Projects { get; set; }

		/// <summary>
		/// Executes this instance.
		/// </summary>
		public override bool Execute() {
			foreach (ITaskItem taskItem in this.Projects) {
				switch (GetClassification(taskItem)) {
					case ProjectClassification.VS2010Project:
						this.Log.LogMessage(MessageImportance.Low, "Downgrading project \"{0}\".", taskItem.ItemSpec);
						Project project = new Project();
						project.Load(taskItem.ItemSpec);
						project.DefaultToolsVersion = "3.5";

						string projectTypeGuids = project.GetEvaluatedProperty("ProjectTypeGuids");
						if (!string.IsNullOrEmpty(projectTypeGuids)) {
							projectTypeGuids = projectTypeGuids.Replace("{F85E285D-A4E0-4152-9332-AB1D724D3325}", "{603c0e0b-db56-11dc-be95-000d561079b0}");
							project.SetProperty("ProjectTypeGuids", projectTypeGuids);
						}

						// Web projects usually have an import that includes these substrings
						foreach (Import import in project.Imports) {
							import.ProjectPath = import.ProjectPath
								.Replace("$(MSBuildExtensionsPath32)", "$(MSBuildExtensionsPath)")
								.Replace("VisualStudio\\v10.0", "VisualStudio\\v9.0");
						}

						// VS2010 won't let you have a System.Core reference, but VS2008 requires it.
						BuildItemGroup references = project.GetEvaluatedItemsByName("Reference");
						if (!references.Cast<BuildItem>().Any(item => item.FinalItemSpec.StartsWith("System.Core", StringComparison.OrdinalIgnoreCase))) {
							project.AddNewItem("Reference", "System.Core");
						}

						project.Save(taskItem.ItemSpec);
						break;
					case ProjectClassification.VS2010Solution:
						this.Log.LogMessage(MessageImportance.Low, "Downgrading solution \"{0}\".", taskItem.ItemSpec);
						string[] contents = File.ReadAllLines(taskItem.ItemSpec);
						if (contents[1] != "Microsoft Visual Studio Solution File, Format Version 11.00" ||
							contents[2] != "# Visual Studio 2010") {
							this.Log.LogError("Unrecognized solution file header in \"{0}\".", taskItem.ItemSpec);
							break;
						}

						contents[1] = "Microsoft Visual Studio Solution File, Format Version 10.00";
						contents[2] = "# Visual Studio 2008";

						for (int i = 3; i < contents.Length; i++) {
							contents[i] = contents[i].Replace("TargetFrameworkMoniker = \".NETFramework,Version%3Dv", "TargetFramework = \"");
						}

						File.WriteAllLines(taskItem.ItemSpec, contents);
						break;
					default:
						this.Log.LogWarning("Unrecognized project type for \"{0}\".", taskItem.ItemSpec);
						break;
				}
			}

			return !this.Log.HasLoggedErrors;
		}

		private static ProjectClassification GetClassification(ITaskItem taskItem) {
			if (Path.GetExtension(taskItem.ItemSpec).EndsWith("proj", StringComparison.OrdinalIgnoreCase)) {
				return ProjectClassification.VS2010Project;
			} else if (Path.GetExtension(taskItem.ItemSpec).Equals(".sln", StringComparison.OrdinalIgnoreCase)) {
				return ProjectClassification.VS2010Solution;
			} else {
				return ProjectClassification.Unrecognized;
			}
		}

		private enum ProjectClassification {
			VS2010Project,
			VS2010Solution,
			Unrecognized,
		}
	}
}
