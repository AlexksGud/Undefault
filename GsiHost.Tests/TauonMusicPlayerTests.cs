using System.Net;
using System.Text;
using Core.Music;
using FluentAssertions;
using GsiHost.Players;
using Microsoft.Extensions.Logging.Abstractions;

namespace GsiHost.Tests;

public sealed class TauonMusicPlayerTests
{
    private const string BaseUri = "http://127.0.0.1:7814/";

    [Fact]
    public async Task PlayAsync_SendsGetApi1Play()
    {
        var (player, handler, _) = CreatePlayer(OkResponder());

        var result = await player.PlayAsync();

        result.Should().Be(MusicCommandResult.Applied);
        AssertSingleGet(handler, $"{BaseUri}api1/play");
    }

    [Fact]
    public async Task PauseAsync_SendsGetApi1Pause()
    {
        var (player, handler, _) = CreatePlayer(OkResponder());

        var result = await player.PauseAsync();

        result.Should().Be(MusicCommandResult.Applied);
        AssertSingleGet(handler, $"{BaseUri}api1/pause");
    }

    [Theory]
    [InlineData("paused")]
    [InlineData("stopped")]
    public async Task ResumeAsync_WhenPausedOrStopped_SendsStatusThenPlay(string status)
    {
        var (player, handler, _) = CreatePlayer(StatusThenOk(StatusJson(status)));

        var result = await player.ResumeAsync();

        result.Should().Be(MusicCommandResult.Applied);
        handler.Requests.Should().HaveCount(2);
        AssertGet(handler.Requests[0], $"{BaseUri}api1/status");
        AssertGet(handler.Requests[1], $"{BaseUri}api1/play");
    }

    [Fact]
    public async Task ResumeAsync_WhenPlaying_SendsStatusOnly()
    {
        var (player, handler, _) = CreatePlayer(StatusThenOk(StatusJson("playing")));

        var result = await player.ResumeAsync();

        result.Should().Be(MusicCommandResult.Applied);
        AssertSingleGet(handler, $"{BaseUri}api1/status");
    }

    [Fact]
    public async Task NextAsync_SendsGetApi1Next()
    {
        var (player, handler, _) = CreatePlayer(OkResponder());

        var result = await player.NextAsync();

        result.Should().Be(MusicCommandResult.Applied);
        AssertSingleGet(handler, $"{BaseUri}api1/next");
    }

    [Fact]
    public async Task PreviousAsync_SendsGetApi1Back()
    {
        var (player, handler, _) = CreatePlayer(OkResponder());

        var result = await player.PreviousAsync();

        result.Should().Be(MusicCommandResult.Applied);
        AssertSingleGet(handler, $"{BaseUri}api1/back");
    }

    [Theory]
    [InlineData("playing", PlaybackStatus.Playing)]
    [InlineData("paused", PlaybackStatus.Paused)]
    [InlineData("stopped", PlaybackStatus.Stopped)]
    public async Task GetStateAsync_MapsKnownStatusTrackAndVolume(string status, PlaybackStatus expected)
    {
        var json = """{"status":"STATUS","id":123,"title":"Song","artist":"Artist","album":"Album","volume":50}"""
            .Replace("STATUS", status, StringComparison.Ordinal);
        var (player, handler, _) = CreatePlayer(_ => Json(json));

        var state = await player.GetStateAsync();

        AssertSingleGet(handler, $"{BaseUri}api1/status");
        state.Should().NotBeNull();
        state!.Status.Should().Be(expected);
        state.VolumePercent.Should().Be(50);
        state.Track.Should().NotBeNull();
        state.Track!.Id.Should().Be("123");
        state.Track.Title.Should().Be("Song");
        state.Track.Artist.Should().Be("Artist");
        state.Track.Album.Should().Be("Album");
    }

    [Fact]
    public async Task IsAvailableAsync_WhenStatusJsonIsParseable_ReturnsTrue()
    {
        var (player, handler, _) = CreatePlayer(_ => Json(StatusJson("playing")));

        var available = await player.IsAvailableAsync();

        available.Should().BeTrue();
        AssertSingleGet(handler, $"{BaseUri}api1/status");
    }

    [Fact]
    public async Task GetStateAsync_WhenStatusIsUnknown_MapsUnknown()
    {
        var (player, _, _) = CreatePlayer(_ => Json("""{"status":"buffering","title":"x"}"""));

        var state = await player.GetStateAsync();

        state.Should().NotBeNull();
        state!.Status.Should().Be(PlaybackStatus.Unknown);
        state.Track!.Title.Should().Be("x");
    }

    [Fact]
    public async Task Timeout_IsUnavailableGetStateNullAndPlayDoesNotThrow()
    {
        // Stub throws TaskCanceledException; this does not wait on HttpClient.Timeout.
        var (player, _, _) = CreatePlayer(
            sendException: new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."));

        var available = await player.IsAvailableAsync();
        var state = await player.GetStateAsync();
        var play = await player.PlayAsync();

        available.Should().BeFalse();
        state.Should().BeNull();
        AssertNonApplied(play, MusicCommandOutcome.Failed);
    }

    [Fact]
    public async Task ConnectionRefused_IsUnavailableGetStateNullAndPlayDoesNotThrow()
    {
        var (player, _, _) = CreatePlayer(sendException: new HttpRequestException("Connection refused"));

        var available = await player.IsAvailableAsync();
        var state = await player.GetStateAsync();
        var play = await player.PlayAsync();

        available.Should().BeFalse();
        state.Should().BeNull();
        AssertNonApplied(play, MusicCommandOutcome.Unavailable);
    }

    [Fact]
    public async Task Http404_IsUnavailableAndGetStateNull()
    {
        var (player, _, _) = CreatePlayer(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        (await player.IsAvailableAsync()).Should().BeFalse();
        (await player.GetStateAsync()).Should().BeNull();
        AssertNonApplied(await player.PlayAsync(), MusicCommandOutcome.Unsupported);
    }

    [Fact]
    public async Task MalformedJson_IsUnavailableAndGetStateNull()
    {
        var (player, _, _) = CreatePlayer(_ => Text("{ not json", "application/json"));

        (await player.IsAvailableAsync()).Should().BeFalse();
        (await player.GetStateAsync()).Should().BeNull();
        AssertNonApplied(await player.PlayAsync(), MusicCommandOutcome.Rejected);
    }

    [Fact]
    public async Task UnexpectedNonJsonSuccessBody_IsUnavailableAndGetStateNull()
    {
        var (player, _, _) = CreatePlayer(_ => Text("OK", "text/plain"));

        (await player.IsAvailableAsync()).Should().BeFalse();
        (await player.GetStateAsync()).Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task SetVolumeAsync_WhenInRange_SendsSetVolumePath(int volume)
    {
        var (player, handler, _) = CreatePlayer(OkResponder());

        var result = await player.SetVolumeAsync(volume);

        result.Should().Be(MusicCommandResult.Applied);
        AssertSingleGet(handler, $"{BaseUri}api1/setvolume/{volume}");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task SetVolumeAsync_WhenOutOfRange_Throws(int volume)
    {
        var (player, handler, _) = CreatePlayer(OkResponder());

        var act = async () => await player.SetVolumeAsync(volume);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithParameterName("volumePercent");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateClient_IsInvokedOncePerTransportOrStatusCall()
    {
        var (player, handler, factory) = CreatePlayer(OkResponder());

        await player.PlayAsync();
        factory.CreateClientCalls.Should().Be(1);

        await player.PauseAsync();
        factory.CreateClientCalls.Should().Be(2);

        await player.GetStateAsync();
        factory.CreateClientCalls.Should().Be(3);

        handler.Requests.Should().HaveCount(3);
    }

    private static (TauonMusicPlayer Player, StubHandler Handler, CountingHttpClientFactory Factory) CreatePlayer(
        Func<HttpRequestMessage, HttpResponseMessage>? responder = null,
        Exception? sendException = null)
    {
        var handler = new StubHandler
        {
            Responder = responder,
            SendException = sendException
        };
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri(BaseUri)
        };
        var factory = new CountingHttpClientFactory(http);
        var player = new TauonMusicPlayer(factory, NullLogger<TauonMusicPlayer>.Instance);
        return (player, handler, factory);
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> OkResponder()
        => _ => new HttpResponseMessage(HttpStatusCode.OK);

    private static Func<HttpRequestMessage, HttpResponseMessage> StatusThenOk(string statusJson)
        => request =>
        {
            if (request.RequestUri!.AbsolutePath.Equals("/api1/status", StringComparison.OrdinalIgnoreCase))
            {
                return Json(statusJson);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        };

    private static string StatusJson(string status)
        => $$"""{"status":"{{status}}","id":1,"title":"t","artist":"a","album":"b","volume":40}""";

    private static HttpResponseMessage Json(string json)
        => Text(json, "application/json");

    private static HttpResponseMessage Text(string body, string mediaType)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, mediaType)
        };

    private static void AssertSingleGet(StubHandler handler, string absoluteUri)
    {
        handler.Requests.Should().ContainSingle();
        AssertGet(handler.Requests[0], absoluteUri);
    }

    private static void AssertGet((HttpMethod Method, Uri? Uri) request, string absoluteUri)
    {
        request.Method.Should().Be(HttpMethod.Get);
        request.Uri.Should().NotBeNull();
        request.Uri!.AbsoluteUri.Should().Be(absoluteUri);
    }

    private static void AssertNonApplied(MusicCommandResult result, MusicCommandOutcome outcome)
    {
        result.Outcome.Should().Be(outcome);
        result.Reason.Should().NotBeNullOrWhiteSpace();
    }

    private sealed class CountingHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _http;

        public CountingHttpClientFactory(HttpClient http)
        {
            _http = http;
        }

        public int CreateClientCalls { get; private set; }

        public HttpClient CreateClient(string name)
        {
            name.Should().Be(TauonMusicPlayer.HttpClientName);
            CreateClientCalls++;
            return _http;
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri? Uri)> Requests { get; } = new();

        public Func<HttpRequestMessage, HttpResponseMessage>? Responder { get; set; }

        public Exception? SendException { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri));
            if (SendException is not null)
            {
                throw SendException;
            }

            if (Responder is null)
            {
                throw new InvalidOperationException("No responder configured.");
            }

            return Task.FromResult(Responder(request));
        }
    }
}
