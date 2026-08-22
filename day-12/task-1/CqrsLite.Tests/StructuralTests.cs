using System.Reflection;
using CqrsLite.Domain;
using CqrsLite.Features.Quotes.Commands;
using CqrsLite.Features.Quotes.Queries;

namespace CqrsLite.Tests;

// These enforce interpretation 2 - that the command/query split is structurally real, not
// just described in prose. Some checks use reflection over public method signatures
// (the API boundary between the two paths); others parse the actual .cs source text, since
// a signature check alone can't prove which LINQ operators a method body used.
public class StructuralTests
{
    [Fact]
    public void Command_handler_signature_never_mentions_the_read_model_types()
    {
        var forbidden = new[] { typeof(QuoteWallQuery), typeof(QuoteWallItem) };

        foreach (var method in PublicDeclaredMethods(typeof(SubmitQuoteHandler)))
        {
            AssertSignatureExcludes(method, forbidden);
        }
    }

    [Fact]
    public void Query_handler_signature_never_returns_or_takes_a_write_entity_or_command_type()
    {
        var forbidden = new[] { typeof(Quote), typeof(Author), typeof(SubmitQuoteCommand), typeof(SubmitQuoteResult) };

        foreach (var method in PublicDeclaredMethods(typeof(QuoteWallHandler)))
        {
            AssertSignatureExcludes(method, forbidden);
        }
    }

    [Fact]
    public void Command_and_query_handlers_live_in_separate_namespaces()
    {
        Assert.Equal("CqrsLite.Features.Quotes.Commands", typeof(SubmitQuoteHandler).Namespace);
        Assert.Equal("CqrsLite.Features.Quotes.Queries", typeof(QuoteWallHandler).Namespace);
        Assert.NotEqual(typeof(SubmitQuoteHandler).Namespace, typeof(QuoteWallHandler).Namespace);
    }

    [Fact]
    public void Commands_and_queries_live_in_separate_folders_on_disk_with_no_overlap()
    {
        var commandsDir = TaskPaths.SourceFile("Features", "Quotes", "Commands");
        var queriesDir = TaskPaths.SourceFile("Features", "Quotes", "Queries");

        Assert.True(Directory.Exists(commandsDir), $"Expected {commandsDir} to exist.");
        Assert.True(Directory.Exists(queriesDir), $"Expected {queriesDir} to exist.");

        var commandFiles = Directory.GetFiles(commandsDir, "*.cs").Select(Path.GetFileName).ToList();
        var queryFiles = Directory.GetFiles(queriesDir, "*.cs").Select(Path.GetFileName).ToList();

        Assert.Contains("SubmitQuoteHandler.cs", commandFiles);
        Assert.Contains("QuoteWallHandler.cs", queryFiles);
        Assert.Empty(commandFiles.Intersect(queryFiles));
    }

    [Fact]
    public void Command_source_files_never_mention_the_read_model_types_textually()
    {
        var commandsDir = TaskPaths.SourceFile("Features", "Quotes", "Commands");
        foreach (var file in Directory.GetFiles(commandsDir, "*.cs"))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("QuoteWallItem", text);
            Assert.DoesNotContain("QuoteWallQuery", text);
            Assert.DoesNotContain("QuoteWallHandler", text);
        }
    }

    [Fact]
    public void Query_source_files_never_mention_the_write_command_types_textually()
    {
        var queriesDir = TaskPaths.SourceFile("Features", "Quotes", "Queries");
        foreach (var file in Directory.GetFiles(queriesDir, "*.cs"))
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("SubmitQuoteCommand", text);
            Assert.DoesNotContain("SubmitQuoteResult", text);
            Assert.DoesNotContain("SubmitQuoteHandler", text);
        }
    }

    [Fact]
    public void Query_handler_source_uses_AsNoTracking_before_a_Select_projection_and_never_bulk_materializes_first()
    {
        var path = TaskPaths.SourceFile("Features", "Quotes", "Queries", "QuoteWallHandler.cs");
        var text = File.ReadAllText(path);

        // Code only - strip "//" line comments first, so a doc comment that merely
        // mentions AsNoTracking/Select in prose can never make this pass vacuously.
        var codeOnly = string.Join('\n', text.Split('\n').Select(StripLineComment));

        Assert.Contains(".AsNoTracking()", codeOnly);
        Assert.Contains(".Select(", codeOnly);
        Assert.DoesNotContain("Quotes.ToList()", codeOnly);
        Assert.DoesNotContain("Quotes.ToListAsync()", codeOnly);

        var noTrackingIndex = codeOnly.IndexOf(".AsNoTracking()", StringComparison.Ordinal);
        var selectIndex = codeOnly.IndexOf(".Select(", StringComparison.Ordinal);
        Assert.True(noTrackingIndex >= 0 && selectIndex >= 0 && noTrackingIndex < selectIndex,
            "Expected .AsNoTracking() to appear in actual code, before the Select projection, in QuoteWallHandler.cs.");
    }

    private static string StripLineComment(string line)
    {
        var index = line.IndexOf("//", StringComparison.Ordinal);
        return index >= 0 ? line[..index] : line;
    }

    private static IEnumerable<MethodInfo> PublicDeclaredMethods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

    private static void AssertSignatureExcludes(MethodInfo method, Type[] forbidden)
    {
        Assert.DoesNotContain(method.ReturnType, forbidden);

        if (method.ReturnType.IsGenericType)
        {
            foreach (var arg in method.ReturnType.GetGenericArguments())
            {
                Assert.DoesNotContain(arg, forbidden);
            }
        }

        foreach (var parameter in method.GetParameters())
        {
            Assert.DoesNotContain(parameter.ParameterType, forbidden);
        }
    }
}
