using System.Net.Http.Json;
using DocAgent.Tests.Support;
using FluentAssertions;
using Reqnroll;

namespace DocAgent.Tests.Steps;

[Binding]
public class DocumentSteps(ApiContext api)
{
    private Answer? _answer;

    [When("I upload {string} with project {string}, customer {string} and type {string}")]
    public async Task Upload(string file, string project, string customer, string type) =>
        api.LastResponse = await api.Client.PostAsJsonAsync("/documents",
            new { fileName = file, project, customer, type, content = $"Text of {file}." });

    [Then("the response status is {int}")]
    public void Status(int code) => ((int)api.LastResponse!.StatusCode).Should().Be(code);

    [Then("the document is listed under project {string}")]
    public async Task Listed(string project)
    {
        var list = await api.Client.GetFromJsonAsync<List<DocumentRow>>($"/documents?project={project}");
        list.Should().ContainSingle().Which.Project.Should().Be(project);
    }

    [Given("a document {string} in project {string} containing {string}")]
    public async Task GivenDocument(string file, string project, string text) =>
        (await api.Client.PostAsJsonAsync("/documents",
            new { fileName = file, project, customer = "Acme", type = "Contract", content = text }))
        .EnsureSuccessStatusCode();

    [When("I ask {string}")]
    public async Task Ask(string q)
    {
        var response = await api.Client.PostAsJsonAsync("/ask", new { question = q });
        response.EnsureSuccessStatusCode();
        _answer = await response.Content.ReadFromJsonAsync<Answer>();
    }

    [Then("the answer cites {string}")]
    public void Cites(string file) => _answer!.Citations.Should().Contain(file);

    [Then("the answer says it could not find this in the documents")]
    public void Refuses()
    {
        _answer!.Citations.Should().BeEmpty();
        _answer.Text.Should().Be(DocAgent.Core.Rag.AnswerService.NotFound);
    }

    public record Answer(string Text, List<string> Citations);
    public record DocumentRow(int Id, string FileName, string Project, string Customer, string Type);
}
