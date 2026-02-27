using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tech901.IdPhoto.Core.Enums;
using Tech901.IdPhoto.Core.Services;
using Xunit;

namespace Tech901.IdPhoto.Core.Tests;

public class RosterServiceTests : IDisposable
{
    private readonly RosterService _sut;
    private readonly List<string> _tempFiles = [];

    public RosterServiceTests()
    {
        _sut = new RosterService(NullLogger<RosterService>.Instance);
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
        {
            if (File.Exists(f)) File.Delete(f);
        }
    }

    private string CreateTempCsv(string content)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private string CreateTempCsv(string content, Encoding encoding)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, content, encoding);
        _tempFiles.Add(path);
        return path;
    }

    [Fact]
    public async Task LoadRosterAsync_WithValidCsv_LoadsStudents()
    {
        var csv = "StudentId,FirstName,LastName\n001,John,Doe\n002,Jane,Smith\n";
        var path = CreateTempCsv(csv);

        await _sut.LoadRosterAsync(path);

        var students = _sut.GetAllStudents();
        students.Should().HaveCount(2);
        students[0].StudentId.Should().Be("001");
        students[0].FirstName.Should().Be("John");
        students[0].LastName.Should().Be("Doe");
        students[1].StudentId.Should().Be("002");
    }

    [Fact]
    public async Task LoadRosterAsync_MissingRequiredColumns_Throws()
    {
        var csv = "StudentId,Name\n001,John\n";
        var path = CreateTempCsv(csv);

        var act = () => _sut.LoadRosterAsync(path);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*missing required columns*");
    }

    [Fact]
    public async Task FindMatches_ExactName_ReturnsHighConfidence()
    {
        var csv = "StudentId,FirstName,LastName\n001,John,Doe\n002,Jane,Smith\n";
        var path = CreateTempCsv(csv);
        await _sut.LoadRosterAsync(path);

        var matches = _sut.FindMatches("John Doe");

        matches.Should().ContainSingle();
        matches[0].Student.StudentId.Should().Be("001");
        matches[0].Confidence.Should().Be(MatchConfidence.High);
    }

    [Fact]
    public async Task FindMatches_PartialName_ReturnsMultipleMatches()
    {
        var csv = "StudentId,FirstName,LastName\n001,John,Smith\n002,John,Jones\n003,Alice,Williams\n";
        var path = CreateTempCsv(csv);
        await _sut.LoadRosterAsync(path);

        var matches = _sut.FindMatches("John Smith");

        matches.Should().HaveCountGreaterThanOrEqualTo(1);
        matches.Should().Contain(m => m.Student.StudentId == "001");
    }

    [Fact]
    public async Task FindMatches_ExcludesCompletedStudents()
    {
        var csv = "StudentId,FirstName,LastName\n001,John,Doe\n002,Jane,Smith\n";
        var path = CreateTempCsv(csv);
        await _sut.LoadRosterAsync(path);
        _sut.MarkCompleted("001");

        var matches = _sut.FindMatches("John Doe");

        matches.Should().BeEmpty();
    }

    [Fact]
    public void MarkCompleted_And_IsCompleted_WorkCorrectly()
    {
        _sut.MarkCompleted("S001");

        _sut.IsCompleted("S001").Should().BeTrue();
        _sut.IsCompleted("S002").Should().BeFalse();
    }

    [Fact]
    public async Task GetSessionState_ReturnsCorrectCounts()
    {
        var csv = "StudentId,FirstName,LastName\n001,John,Doe\n002,Jane,Smith\n003,Alice,Williams\n";
        var path = CreateTempCsv(csv);
        await _sut.LoadRosterAsync(path);
        _sut.MarkCompleted("001");

        var state = _sut.GetSessionState();

        state.TotalStudents.Should().Be(3);
        state.CompletedStudentIds.Should().HaveCount(1);
        state.CompletedStudentIds.Should().Contain("001");
    }

    // --- New edge-case tests ---

    [Fact]
    public async Task EmptyStudentId_ReportsRowError()
    {
        var csv = "StudentId,FirstName,LastName\n,John,Doe\n";
        var path = CreateTempCsv(csv);

        var result = await _sut.LoadRosterAsync(path);

        result.ImportedCount.Should().Be(0);
        result.SkippedCount.Should().Be(1);
        result.Errors.Should().ContainSingle(e => e.Message.Contains("StudentId"));
        result.Errors[0].RowNumber.Should().Be(2);
    }

    [Fact]
    public async Task EmptyFirstName_ReportsRowError()
    {
        var csv = "StudentId,FirstName,LastName\n001,,Doe\n";
        var path = CreateTempCsv(csv);

        var result = await _sut.LoadRosterAsync(path);

        result.ImportedCount.Should().Be(0);
        result.SkippedCount.Should().Be(1);
        result.Errors.Should().ContainSingle(e => e.Message.Contains("FirstName"));
    }

    [Fact]
    public async Task EmptyLastName_ReportsRowError()
    {
        var csv = "StudentId,FirstName,LastName\n001,John,\n";
        var path = CreateTempCsv(csv);

        var result = await _sut.LoadRosterAsync(path);

        result.ImportedCount.Should().Be(0);
        result.SkippedCount.Should().Be(1);
        result.Errors.Should().ContainSingle(e => e.Message.Contains("LastName"));
    }

    [Fact]
    public async Task AllFieldsBlank_SkippedAsBlankRow()
    {
        var csv = "StudentId,FirstName,LastName\n,,\n001,John,Doe\n";
        var path = CreateTempCsv(csv);

        var result = await _sut.LoadRosterAsync(path);

        result.ImportedCount.Should().Be(1);
        result.SkippedCount.Should().Be(1);
        result.Warnings.Should().ContainSingle(w => w.Message.Contains("Blank row"));
    }

    [Fact]
    public async Task BlankLinesBetweenRows_SkipsGracefully()
    {
        var csv = "StudentId,FirstName,LastName\n001,John,Doe\n,,\n002,Jane,Smith\n";
        var path = CreateTempCsv(csv);

        var result = await _sut.LoadRosterAsync(path);

        result.ImportedCount.Should().Be(2);
        result.SkippedCount.Should().Be(1);
        _sut.GetAllStudents().Should().HaveCount(2);
    }

    [Fact]
    public async Task DuplicateStudentIds_SkipsSecondAndWarns()
    {
        var csv = "StudentId,FirstName,LastName\n001,John,Doe\n001,Jane,Smith\n";
        var path = CreateTempCsv(csv);

        var result = await _sut.LoadRosterAsync(path);

        result.ImportedCount.Should().Be(1);
        result.DuplicateCount.Should().Be(1);
        result.SkippedCount.Should().Be(1);
        result.Warnings.Should().ContainSingle(w => w.Message.Contains("Duplicate"));
        _sut.GetAllStudents().Should().ContainSingle();
        _sut.GetAllStudents()[0].FirstName.Should().Be("John"); // First wins
    }

    [Fact]
    public async Task AlternateColumnNames_MapsCorrectly()
    {
        var csv = "Student ID,First Name,Last Name\n001,John,Doe\n";
        var path = CreateTempCsv(csv);

        var result = await _sut.LoadRosterAsync(path);

        result.ImportedCount.Should().Be(1);
        var student = _sut.GetAllStudents()[0];
        student.StudentId.Should().Be("001");
        student.FirstName.Should().Be("John");
        student.LastName.Should().Be("Doe");
    }

    [Fact]
    public async Task UnderscoreColumnNames_MapsCorrectly()
    {
        var csv = "student_id,first_name,last_name,preferred_name\n001,John,Doe,JD\n";
        var path = CreateTempCsv(csv);

        var result = await _sut.LoadRosterAsync(path);

        result.ImportedCount.Should().Be(1);
        var student = _sut.GetAllStudents()[0];
        student.StudentId.Should().Be("001");
        student.FirstName.Should().Be("John");
        student.LastName.Should().Be("Doe");
        student.PreferredName.Should().Be("JD");
    }

    [Fact]
    public async Task IdColumnAlias_MapsToStudentId()
    {
        var csv = "ID,FirstName,LastName\n001,John,Doe\n";
        var path = CreateTempCsv(csv);

        var result = await _sut.LoadRosterAsync(path);

        result.ImportedCount.Should().Be(1);
        _sut.GetAllStudents()[0].StudentId.Should().Be("001");
    }

    [Fact]
    public async Task Utf8WithBom_ParsesCorrectly()
    {
        var csv = "StudentId,FirstName,LastName\n001,John,Doe\n";
        var path = CreateTempCsv(csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var result = await _sut.LoadRosterAsync(path);

        result.ImportedCount.Should().Be(1);
        _sut.GetAllStudents()[0].StudentId.Should().Be("001");
    }

    [Fact]
    public async Task MixedErrors_ReturnsAccurateStatistics()
    {
        var csv = "StudentId,FirstName,LastName\n001,John,Doe\n,,\n,Jane,Smith\n002,Alice,Williams\n002,Bob,Jones\n";
        var path = CreateTempCsv(csv);

        var result = await _sut.LoadRosterAsync(path);

        result.TotalRowsProcessed.Should().Be(5);
        result.ImportedCount.Should().Be(2); // 001 + 002
        result.SkippedCount.Should().Be(3); // blank + missing id + duplicate
        result.DuplicateCount.Should().Be(1); // second 002
        result.Errors.Should().ContainSingle(); // missing StudentId for Jane
        result.Warnings.Should().HaveCount(2); // blank row + duplicate
        result.IsSuccess.Should().BeFalse();
        result.HasWarnings.Should().BeTrue();
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
    }
}
