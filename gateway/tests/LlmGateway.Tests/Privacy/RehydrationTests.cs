using System.Text.Json.Nodes;
using LlmGateway.Core.Chat;
using LlmGateway.Core.Privacy;

namespace LlmGateway.Tests.Privacy;

public sealed class RehydrationTests
{
    private static PiiVault VaultWith(params (PiiKind Kind, string Value)[] values)
    {
        var vault = new PiiVault();
        foreach (var (kind, value) in values)
        {
            vault.Protect(kind, value);
        }
        return vault;
    }

    [Fact]
    public void Restores_known_placeholders_and_keeps_unknown_ones()
    {
        var vault = VaultWith((PiiKind.Email, "ana@x.com"));

        var text = PiiRehydrator.Rehydrate("Enviado para [EMAIL_1] e [EMAIL_9].", vault);

        Assert.Equal("Enviado para ana@x.com e [EMAIL_9].", text);
    }

    [Fact]
    public void Streaming_restores_placeholders_split_across_chunks()
    {
        var rehydrator = new StreamingRehydrator(VaultWith((PiiKind.Email, "ana@x.com"), (PiiKind.Cpf, "529.982.247-25")));

        var output = string.Concat(
            rehydrator.Push("Seu e-mail é [EM"),
            rehydrator.Push("AIL_1] e o CPF ["),
            rehydrator.Push("CPF_"),
            rehydrator.Push("1]."),
            rehydrator.Flush());

        Assert.Equal("Seu e-mail é ana@x.com e o CPF 529.982.247-25.", output);
    }

    [Fact]
    public void Streaming_holds_only_while_a_placeholder_is_possible()
    {
        var rehydrator = new StreamingRehydrator(VaultWith((PiiKind.Email, "ana@x.com")));

        Assert.Equal("Veja ", rehydrator.Push("Veja [no"));
        Assert.Equal("[nota] aqui", rehydrator.Push("ta] aqui"));
        Assert.Equal(string.Empty, rehydrator.Push("["));
        Assert.Equal("[" + new string('x', 30), rehydrator.Push(new string('x', 30)));
        Assert.False(rehydrator.HasPending);
    }

    [Fact]
    public void Flush_releases_incomplete_brackets_at_the_end()
    {
        var rehydrator = new StreamingRehydrator(new PiiVault());

        Assert.Equal("fim ", rehydrator.Push("fim [EMA"));
        Assert.Equal("[EMA", rehydrator.Flush());
    }

    [Fact]
    public void Stream_rehydration_flushes_pending_text_on_finish_chunk()
    {
        var rehydration = new StreamRehydration(VaultWith((PiiKind.Phone, "(11) 98765-4321")));
        var first = Chunk("ligue para [TELEF", null);
        var last = Chunk("ONE_1", "stop");

        rehydration.Apply(first);
        rehydration.Apply(last);

        Assert.Equal("ligue para ", first["choices"]![0]!["delta"]!["content"]!.GetValue<string>());
        Assert.Equal("[TELEFONE_1", last["choices"]![0]!["delta"]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void Rehydrates_message_content_and_tool_arguments()
    {
        var vault = VaultWith((PiiKind.Email, "ana@x.com"));
        var response = new JsonObject
        {
            ["choices"] = new JsonArray(new JsonObject
            {
                ["message"] = new JsonObject
                {
                    ["content"] = "Contato: [EMAIL_1]",
                    ["tool_calls"] = new JsonArray(new JsonObject
                    {
                        ["function"] = new JsonObject { ["name"] = "enviar", ["arguments"] = "{\"to\":\"[EMAIL_1]\"}" },
                    }),
                },
            }),
        };

        ChatPayload.Rehydrate(response, vault);

        var message = response["choices"]![0]!["message"]!;
        Assert.Equal("Contato: ana@x.com", message["content"]!.GetValue<string>());
        Assert.Equal("{\"to\":\"ana@x.com\"}", message["tool_calls"]![0]!["function"]!["arguments"]!.GetValue<string>());
    }

    private static JsonObject Chunk(string content, string? finish) => new()
    {
        ["choices"] = new JsonArray(new JsonObject
        {
            ["index"] = 0,
            ["delta"] = new JsonObject { ["content"] = content },
            ["finish_reason"] = finish,
        }),
    };
}
