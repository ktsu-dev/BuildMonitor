// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.BuildMonitor.Test;

using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
/// Tests for the decision half of the ImGuiTableColumn layout workaround.
/// </summary>
/// <remarks>
/// <c>SaveColumnWidth</c> reads a float out of ImGui's native structs by pointer arithmetic. If the
/// stride it uses is wrong it reads from the wrong address, so the rule that picks that stride is
/// worth pinning. <c>ResolveNativeColumnStride</c> exists as a separate, plain-integer method for
/// exactly this reason: the probe around it needs <c>unsafe</c>, reflection and a real
/// Hexa.NET.ImGui type, none of which a test can vary, while the decision can be driven through
/// every branch -- including the ones unreachable with the binding currently referenced.
/// </remarks>
[TestClass]
public sealed class ColumnStrideTests
{
	// Measured against Hexa.NET.ImGui 2.2.9.
	private const int MeasuredCSharpSize = 108;
	private const int MeasuredWidthGivenOffset = 4;
	private const int NarrowFieldCount = 8;

	/// <summary>
	/// The layout actually shipped by the referenced binding must produce the stride the
	/// workaround has always used: 108 + 8.
	/// </summary>
	[TestMethod]
	public void TheCurrentlyShippedLayoutResolvesToTheDocumentedStride()
	{
		// Act
		int? stride = BuildMonitor.ResolveNativeColumnStride(
			MeasuredCSharpSize, MeasuredWidthGivenOffset, NarrowFieldCount);

		// Assert
		Assert.AreEqual(116, stride);
	}

	/// <summary>
	/// A binding whose index fields are two bytes apiece matches native, so sizeof is already the
	/// stride and no adjustment must be applied.
	/// </summary>
	[TestMethod]
	public void ACorrectedBindingResolvesToSizeofWithNoAdjustment()
	{
		// Act -- the eight fields now occupy two bytes each, so the struct is 8 bytes larger
		int? stride = BuildMonitor.ResolveNativeColumnStride(
			MeasuredCSharpSize + 8, MeasuredWidthGivenOffset, NarrowFieldCount * 2);

		// Assert
		Assert.AreEqual(MeasuredCSharpSize + 8, stride);
	}

	/// <summary>
	/// A field width that is neither the known-narrow nor the corrected one must be refused rather
	/// than guessed at, since any stride derived from it would be a fabrication.
	/// </summary>
	/// <param name="narrowFieldBytes">A total that matches neither known layout.</param>
	[TestMethod]
	[DataRow(0)]
	[DataRow(4)]
	[DataRow(7)]
	[DataRow(9)]
	[DataRow(12)]
	[DataRow(24)]
	[DataRow(32)]
	public void AnUnrecognisedFieldWidthIsRefused(int narrowFieldBytes)
	{
		// Act
		int? stride = BuildMonitor.ResolveNativeColumnStride(
			MeasuredCSharpSize, MeasuredWidthGivenOffset, narrowFieldBytes);

		// Assert
		Assert.IsNull(stride);
	}

	/// <summary>
	/// A moved WidthGiven means the struct was reordered, so the hardcoded read offset no longer
	/// points at the width and nothing may be read -- whatever the field widths say.
	/// </summary>
	/// <param name="widthGivenOffset">An offset other than the expected one.</param>
	[TestMethod]
	[DataRow(0)]
	[DataRow(2)]
	[DataRow(8)]
	[DataRow(16)]
	public void AMovedWidthGivenOffsetIsRefused(int widthGivenOffset)
	{
		// Act -- field widths are the known-good ones; only the offset moved
		int? stride = BuildMonitor.ResolveNativeColumnStride(
			MeasuredCSharpSize, widthGivenOffset, NarrowFieldCount);

		// Assert
		Assert.IsNull(stride);
	}

	/// <summary>
	/// The offset check must take precedence: a reordered struct is refused even when the field
	/// widths look like a corrected binding.
	/// </summary>
	[TestMethod]
	public void AMovedOffsetIsRefusedEvenWhenTheFieldWidthsLookCorrected()
	{
		// Act
		int? stride = BuildMonitor.ResolveNativeColumnStride(
			MeasuredCSharpSize + 8, widthGivenOffset: 12, narrowFieldBytes: NarrowFieldCount * 2);

		// Assert
		Assert.IsNull(stride);
	}

	/// <summary>
	/// The narrow-binding branch adjusts by exactly the documented difference, whatever the
	/// struct's overall size, so a struct that grows for an unrelated reason still resolves.
	/// </summary>
	/// <param name="csharpSize">A plausible struct size.</param>
	[TestMethod]
	[DataRow(96)]
	[DataRow(108)]
	[DataRow(120)]
	[DataRow(160)]
	public void TheNarrowBindingAdjustmentIsIndependentOfTheStructSize(int csharpSize)
	{
		// Act
		int? stride = BuildMonitor.ResolveNativeColumnStride(
			csharpSize, MeasuredWidthGivenOffset, NarrowFieldCount);

		// Assert
		Assert.AreEqual(csharpSize + 8, stride);
	}
}
