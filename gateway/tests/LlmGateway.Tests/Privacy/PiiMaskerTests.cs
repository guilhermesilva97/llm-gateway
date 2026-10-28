using LlmGateway.Core.Privacy;

namespace LlmGateway.Tests.Privacy;

public sealed class PiiMaskerTests
{
    private readonly PiiMasker _masker = new();

    [Theory]
    [InlineData("Meu e-mail é ana.silva@empresa.com.br", "Meu e-mail é [EMAIL_1]")]
    [InlineData("CPF 529.982.247-25 cadastrado", "CPF [CPF_1] cadastrado")]
    [InlineData("CPF 52998224725", "CPF [CPF_1]")]
    [InlineData("CPF de teste 123.456.789-00", "CPF de teste [CPF_1]")]
    [InlineData("CNPJ 11.222.333/0001-81", "CNPJ [CNPJ_1]")]
    [InlineData("CNPJ 11222333000181", "CNPJ [CNPJ_1]")]
    [InlineData("CNPJ alfanumérico 12.ABC.345/01DE-35", "CNPJ alfanumérico [CNPJ_1]")]
    [InlineData("CNPJ alfanumérico 12ABC34501DE35", "CNPJ alfanumérico [CNPJ_1]")]
    [InlineData("Cartão 4111 1111 1111 1111", "Cartão [CARTAO_1]")]
    [InlineData("Cartão 4111-1111-1111-1111", "Cartão [CARTAO_1]")]
    [InlineData("Cartão 4111111111111111", "Cartão [CARTAO_1]")]
    [InlineData("Ligue (11) 98765-4321", "Ligue [TELEFONE_1]")]
    [InlineData("Ligue +55 11 98765-4321", "Ligue [TELEFONE_1]")]
    [InlineData("Ligue +5511987654321", "Ligue [TELEFONE_1]")]
    [InlineData("Fixo 11 3456-7890", "Fixo [TELEFONE_1]")]
    public void Masks_supported_personal_data(string input, string expected)
    {
        Assert.Equal(expected, _masker.Mask(input, new PiiVault()));
    }

    [Theory]
    [InlineData("Pedido 2024-000123 custou R$ 1.234,56 em 15/03/2025")]
    [InlineData("Atualize para a versão 10.0.14 do SDK")]
    [InlineData("Código de rastreio 12345678901")]
    [InlineData("Cartão de teste inválido 4111 1111 1111 1112")]
    [InlineData("CNPJ sem dígito válido 11222333000180")]
    [InlineData("O resultado foi 42 e o total 1500")]
    public void Leaves_non_personal_data_untouched(string input)
    {
        Assert.Equal(input, _masker.Mask(input, new PiiVault()));
    }

    [Fact]
    public void Reuses_the_same_placeholder_for_repeated_values()
    {
        var vault = new PiiVault();

        var first = _masker.Mask("de ana@x.com para bia@y.com", vault);
        var second = _masker.Mask("responder para ana@x.com", vault);

        Assert.Equal("de [EMAIL_1] para [EMAIL_2]", first);
        Assert.Equal("responder para [EMAIL_1]", second);
        Assert.Equal(2, vault.Count);
    }

    [Fact]
    public void Numbers_placeholders_per_kind_and_counts_them()
    {
        var vault = new PiiVault();

        var masked = _masker.Mask(
            "Cliente 529.982.247-25, e-mail ana@x.com, fone (21) 99999-0000, empresa 11.222.333/0001-81",
            vault);

        Assert.Equal("Cliente [CPF_1], e-mail [EMAIL_1], fone [TELEFONE_1], empresa [CNPJ_1]", masked);
        Assert.Equal(1, vault.CountsByKind[PiiKind.Cpf]);
        Assert.Equal(1, vault.CountsByKind[PiiKind.Email]);
        Assert.Equal(1, vault.CountsByKind[PiiKind.Phone]);
        Assert.Equal(1, vault.CountsByKind[PiiKind.Cnpj]);
    }

    [Fact]
    public void Masking_is_idempotent_over_placeholders()
    {
        var vault = new PiiVault();
        var once = _masker.Mask("ana@x.com e 529.982.247-25", vault);

        var twice = _masker.Mask(once, vault);

        Assert.Equal(once, twice);
        Assert.Equal(2, vault.Count);
    }
}

public sealed class DocumentValidatorsTests
{
    [Theory]
    [InlineData("529.982.247-25", true)]
    [InlineData("52998224725", true)]
    [InlineData("529.982.247-26", false)]
    [InlineData("111.111.111-11", false)]
    [InlineData("1234", false)]
    public void Validates_cpf_check_digits(string cpf, bool valid) =>
        Assert.Equal(valid, DocumentValidators.IsValidCpf(cpf));

    [Theory]
    [InlineData("11.222.333/0001-81", true)]
    [InlineData("11222333000181", true)]
    [InlineData("12.ABC.345/01DE-35", true)]
    [InlineData("12ABC34501DE35", true)]
    [InlineData("12ABC34501DE36", false)]
    [InlineData("00000000000000", false)]
    [InlineData("11.222.333/0001-80", false)]
    public void Validates_numeric_and_alphanumeric_cnpj(string cnpj, bool valid) =>
        Assert.Equal(valid, DocumentValidators.IsValidCnpj(cnpj));

    [Theory]
    [InlineData("4111 1111 1111 1111", true)]
    [InlineData("5500 0000 0000 0004", true)]
    [InlineData("4111 1111 1111 1112", false)]
    [InlineData("4111", false)]
    public void Validates_cards_with_luhn(string card, bool valid) =>
        Assert.Equal(valid, DocumentValidators.IsValidCard(card));
}
