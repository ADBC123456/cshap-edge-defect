using System;
using DiscSorter.Core.Models;
using Xunit;

namespace DiscSorter.Tests
{
    public class ModelTests
    {
        [Theory]
        [InlineData(1, 1, 1)]
        [InlineData(3, 3, 1)]
        [InlineData(4, 1, 2)]
        [InlineData(12, 3, 4)]
        public void SlotNumberMatchesPlcLayout(int slotNo, int row, int column)
        {
            Assert.Equal(slotNo, SlotNumbers.GetSlotNo(row, column));
            Assert.Equal(row, SlotNumbers.GetRow(slotNo));
            Assert.Equal(column, SlotNumbers.GetColumn(slotNo));
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, true)]
        [InlineData(12, true)]
        [InlineData(13, false)]
        public void SlotNumberRange(int slotNo, bool valid)
        {
            Assert.Equal(valid, SlotNumbers.IsValid(slotNo));
        }

        [Theory]
        [InlineData("Q", DefectCategory.Q)]
        [InlineData("h", DefectCategory.H)]
        [InlineData("K", DefectCategory.K)]
        [InlineData("W", DefectCategory.W)]
        [InlineData("NONE", DefectCategory.NG)]
        [InlineData("x", DefectCategory.NG)]
        [InlineData(null, DefectCategory.NG)]
        public void AlgorithmLabelMapsToCategory(string label, DefectCategory expected)
        {
            Assert.Equal(expected, DefectCategories.FromAlgorithmLabel(label));
        }

        [Fact]
        public void CategoryCodesMatchPlcConvention()
        {
            Assert.Equal(1, (int)DefectCategory.Q);
            Assert.Equal(2, (int)DefectCategory.H);
            Assert.Equal(3, (int)DefectCategory.K);
            Assert.Equal(4, (int)DefectCategory.W);
            Assert.Equal(5, (int)DefectCategory.NG);
            Assert.Equal(5, DefectCategories.All.Length);
        }

        [Theory]
        [InlineData(@"E:\data\samples\Q_1.jpg", "Q")]
        [InlineData("NONE_clean.png", "NONE")]
        [InlineData("w_blob.bmp", "W")]
        [InlineData("sample.jpg", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void FileNamePrefixGivesGroundTruthLabel(string path, string expected)
        {
            Assert.Equal(expected, DefectCategories.GetLabelFromFileName(path));
        }

        [Fact]
        public void RecordDisplayTexts()
        {
            InspectionRecord record = new InspectionRecord();
            record.ImagePath = @"C:\images\Q_1.jpg";
            record.TargetSlot = 0;
            record.IsCorrect = false;

            Assert.Equal("Q_1.jpg", record.FileName);
            Assert.Equal("0（人工取件）", record.TargetText);
            Assert.Equal("✗", record.CorrectText);
        }
    }
}
