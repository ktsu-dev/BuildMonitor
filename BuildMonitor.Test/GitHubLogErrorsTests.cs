// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests the error messages read out of a GitHub job log.
/// </summary>
/// <remarks>
/// A compiler-style line was cut at the first colon after the first "error" anywhere in the line,
/// so a path such as <c>src/errors.c</c> left a <c>line:col: error:</c> prefix on the message
/// (ktsu-dev/BuildMonitor#415).
/// </remarks>
[TestClass]
public sealed class GitHubLogErrorsTests
{
	private static void AssertErrors(List<string> actual, params string[] expected) =>
		CollectionAssert.AreEqual(expected, actual);

	[TestMethod]
	public void AnErrorInARepositoryNamedForErrorsKeepsOnlyTheMessage()
	{
		// Arrange
		const string log = "2026-11-11T06:00:00.0000000Z /home/runner/work/error-codes/error-codes/src/parse.c:12:5: error: 'x' undeclared (first use in this function)";

		// Act
		List<string> errors = GitHub.ParseLogForErrors(log);

		// Assert
		AssertErrors(errors, "'x' undeclared (first use in this function)");
	}

	[TestMethod]
	public void AnErrorInAFileNamedForErrorsKeepsOnlyTheMessage()
	{
		// Arrange
		const string log = "2026-11-11T06:00:00.0000000Z src/errors.c:40:1: error: expected ';' before '}' token";

		// Act
		List<string> errors = GitHub.ParseLogForErrors(log);

		// Assert
		AssertErrors(errors, "expected ';' before '}' token");
	}

	[TestMethod]
	public void TheSameErrorAtTwoLocationsInFilesNamedForErrorsIsReportedOnce()
	{
		// Arrange
		const string log = "src/errors.c:40:1: error: expected ';' before '}' token\nsrc/errors.c:52:3: error: expected ';' before '}' token";

		// Act
		List<string> errors = GitHub.ParseLogForErrors(log);

		// Assert
		AssertErrors(errors, "expected ';' before '}' token");
	}

	[TestMethod]
	public void AnErrorInAnOrdinaryPathKeepsOnlyTheMessage()
	{
		// Arrange
		const string log = "2026-11-11T06:00:00.0000000Z /home/runner/work/app/app/src/parse.c:12:5: error: 'y' undeclared";

		// Act
		List<string> errors = GitHub.ParseLogForErrors(log);

		// Assert
		AssertErrors(errors, "'y' undeclared");
	}

	[TestMethod]
	public void AnnotationsAndLeadingErrorLinesAreStillRead()
	{
		// Arrange
		const string log = "##[error]Process completed with exit code 1.\nError: Cannot find module 'x'\nerror: nothing here\nerror:   ";

		// Act
		List<string> errors = GitHub.ParseLogForErrors(log);

		// Assert
		AssertErrors(errors, "Process completed with exit code 1.", "Cannot find module 'x'", "nothing here");
	}
}
