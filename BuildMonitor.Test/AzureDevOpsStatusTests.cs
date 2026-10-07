// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using ktsu.Semantics.Strings;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Covers the provider status after an Azure DevOps request. The request wrapper cleared the status
/// after every action that returned normally, so an error the action reported itself, such as an
/// owner that matches no project, was wiped straight away and never reached the status bar
/// (ktsu-dev/BuildMonitor#304).
/// </summary>
[TestClass]
public sealed class AzureDevOpsStatusTests
{
	private static readonly OwnerName MissingProject = "Missing".As<OwnerName>();

	[TestMethod]
	public async Task AProjectNotFoundErrorSurvivesTheRequestThatReportedIt()
	{
		// Arrange
		AzureDevOps provider = new();

		// Act
		await provider.MakeAzureDevOpsRequestAsync("test/missing", () =>
		{
			provider.ReportProjectNotFound(MissingProject);
			return Task.CompletedTask;
		}).ConfigureAwait(false);

		// Assert
		Assert.AreEqual(ProviderStatus.Error, provider.Status, "The not-found error should still be showing once the request returns");
		Assert.Contains("'Missing' not found", provider.StatusMessage);
	}

	[TestMethod]
	public async Task ALaterSuccessfulRequestClearsTheError()
	{
		// Arrange
		AzureDevOps provider = new();
		await provider.MakeAzureDevOpsRequestAsync("test/missing", () =>
		{
			provider.ReportProjectNotFound(MissingProject);
			return Task.CompletedTask;
		}).ConfigureAwait(false);

		// Act
		await provider.MakeAzureDevOpsRequestAsync("test/ok", () => Task.CompletedTask).ConfigureAwait(false);

		// Assert
		Assert.AreEqual(ProviderStatus.OK, provider.Status);
		Assert.AreEqual(string.Empty, provider.StatusMessage);
	}
}
