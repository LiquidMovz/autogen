﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// OpenAIChatAgentTest.cs

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoGen.OpenAI.Extension;
using AutoGen.Tests;
using Azure.AI.OpenAI;
using FluentAssertions;
using OpenAI;
using OpenAI.Chat;

namespace AutoGen.OpenAI.Tests;

public partial class OpenAIChatAgentTest
{
    /// <summary>
    /// Get the weather for a location.
    /// </summary>
    /// <param name="location">location</param>
    /// <returns></returns>
    [Function]
    public async Task<string> GetWeatherAsync(string location)
    {
        return $"The weather in {location} is sunny.";
    }

    [ApiKeyFact("AZURE_OPENAI_API_KEY", "AZURE_OPENAI_ENDPOINT", "AZURE_OPENAI_DEPLOY_NAME")]
    public async Task BasicConversationTestAsync()
    {
        var deployName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOY_NAME") ?? throw new Exception("Please set AZURE_OPENAI_DEPLOY_NAME environment variable.");
        var openaiClient = CreateOpenAIClientFromAzureOpenAI();
        var openAIChatAgent = new OpenAIChatAgent(
            chatClient: openaiClient.GetChatClient(deployName),
            name: "assistant");

        // By default, OpenAIChatClient supports the following message types
        // - IMessage<ChatRequestMessage>
        var chatMessageContent = MessageEnvelope.Create(new UserChatMessage("Hello"));
        var reply = await openAIChatAgent.SendAsync(chatMessageContent);

        reply.Should().BeOfType<MessageEnvelope<ChatCompletion>>();
        reply.As<MessageEnvelope<ChatCompletion>>().From.Should().Be("assistant");
        reply.As<MessageEnvelope<ChatCompletion>>().Content.Role.Should().Be(ChatMessageRole.Assistant);
        reply.As<MessageEnvelope<ChatCompletion>>().Content.Usage.TotalTokens.Should().BeGreaterThan(0);

        // test streaming
        var streamingReply = openAIChatAgent.GenerateStreamingReplyAsync(new[] { chatMessageContent });

        await foreach (var streamingMessage in streamingReply)
        {
            streamingMessage.Should().BeOfType<MessageEnvelope<StreamingChatCompletionUpdate>>();
            streamingMessage.As<MessageEnvelope<StreamingChatCompletionUpdate>>().From.Should().Be("assistant");
        }
    }

    [ApiKeyFact("AZURE_OPENAI_API_KEY", "AZURE_OPENAI_ENDPOINT", "AZURE_OPENAI_DEPLOY_NAME")]
    public async Task OpenAIChatMessageContentConnectorTestAsync()
    {
        var deployName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOY_NAME") ?? throw new Exception("Please set AZURE_OPENAI_DEPLOY_NAME environment variable.");
        var openaiClient = CreateOpenAIClientFromAzureOpenAI();
        var openAIChatAgent = new OpenAIChatAgent(
            chatClient: openaiClient.GetChatClient(deployName),
            name: "assistant");

        MiddlewareStreamingAgent<OpenAIChatAgent> assistant = openAIChatAgent
            .RegisterMessageConnector();

        var messages = new IMessage[]
        {
            MessageEnvelope.Create(new UserChatMessage("Hello")),
            new TextMessage(Role.Assistant, "Hello", from: "user"),
            new MultiModalMessage(Role.Assistant,
                [
                    new TextMessage(Role.Assistant, "Hello", from: "user"),
                ],
                from: "user"),
        };

        foreach (var message in messages)
        {
            var reply = await assistant.SendAsync(message);

            reply.Should().BeOfType<TextMessage>();
            reply.As<TextMessage>().From.Should().Be("assistant");
        }

        // test streaming
        foreach (var message in messages)
        {
            var reply = assistant.GenerateStreamingReplyAsync([message]);

            await foreach (var streamingMessage in reply)
            {
                streamingMessage.Should().BeOfType<TextMessageUpdate>();
                streamingMessage.As<TextMessageUpdate>().From.Should().Be("assistant");
            }
        }
    }

    [ApiKeyFact("AZURE_OPENAI_API_KEY", "AZURE_OPENAI_ENDPOINT", "AZURE_OPENAI_DEPLOY_NAME")]
    public async Task OpenAIChatAgentToolCallTestAsync()
    {
        var deployName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOY_NAME") ?? throw new Exception("Please set AZURE_OPENAI_DEPLOY_NAME environment variable.");
        var openaiClient = CreateOpenAIClientFromAzureOpenAI();
        var openAIChatAgent = new OpenAIChatAgent(
            chatClient: openaiClient.GetChatClient(deployName),
            name: "assistant");

        var functionCallMiddleware = new FunctionCallMiddleware(
            functions: [this.GetWeatherAsyncFunctionContract]);
        MiddlewareStreamingAgent<OpenAIChatAgent> assistant = openAIChatAgent
            .RegisterMessageConnector();

        assistant.StreamingMiddlewares.Count().Should().Be(1);
        var functionCallAgent = assistant
            .RegisterStreamingMiddleware(functionCallMiddleware);

        var question = "What's the weather in Seattle";
        var messages = new IMessage[]
        {
            MessageEnvelope.Create(new UserChatMessage(question)),
            new TextMessage(Role.Assistant, question, from: "user"),
            new MultiModalMessage(Role.Assistant,
                [
                    new TextMessage(Role.Assistant, question, from: "user"),
                ],
                from: "user"),
        };

        foreach (var message in messages)
        {
            var reply = await functionCallAgent.SendAsync(message);

            reply.Should().BeOfType<ToolCallMessage>();
            reply.As<ToolCallMessage>().From.Should().Be("assistant");
            reply.As<ToolCallMessage>().ToolCalls.Count().Should().Be(1);
            reply.As<ToolCallMessage>().ToolCalls.First().FunctionName.Should().Be(this.GetWeatherAsyncFunctionContract.Name);
        }

        // test streaming
        foreach (var message in messages)
        {
            var reply = functionCallAgent.GenerateStreamingReplyAsync([message]);
            ToolCallMessage? toolCallMessage = null;
            await foreach (var streamingMessage in reply)
            {
                if (streamingMessage is ToolCallMessage finalMessage)
                {
                    toolCallMessage = finalMessage;
                    break;
                }

                streamingMessage.Should().BeOfType<ToolCallMessageUpdate>();
                streamingMessage.As<ToolCallMessageUpdate>().From.Should().Be("assistant");
            }

            toolCallMessage.Should().NotBeNull();
            toolCallMessage!.From.Should().Be("assistant");
            toolCallMessage.ToolCalls.Count().Should().Be(1);
            toolCallMessage.ToolCalls.First().FunctionName.Should().Be(this.GetWeatherAsyncFunctionContract.Name);
        }
    }

    [ApiKeyFact("AZURE_OPENAI_API_KEY", "AZURE_OPENAI_ENDPOINT", "AZURE_OPENAI_DEPLOY_NAME")]
    public async Task OpenAIChatAgentToolCallInvokingTestAsync()
    {
        var deployName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOY_NAME") ?? throw new Exception("Please set AZURE_OPENAI_DEPLOY_NAME environment variable.");
        var openaiClient = CreateOpenAIClientFromAzureOpenAI();
        var openAIChatAgent = new OpenAIChatAgent(
            chatClient: openaiClient.GetChatClient(deployName),
            name: "assistant");

        var functionCallMiddleware = new FunctionCallMiddleware(
            functions: [this.GetWeatherAsyncFunctionContract],
            functionMap: new Dictionary<string, Func<string, Task<string>>> { { this.GetWeatherAsyncFunctionContract.Name!, this.GetWeatherAsyncWrapper } });
        MiddlewareStreamingAgent<OpenAIChatAgent> assistant = openAIChatAgent
            .RegisterMessageConnector();

        var functionCallAgent = assistant
            .RegisterStreamingMiddleware(functionCallMiddleware);

        var question = "What's the weather in Seattle";
        var messages = new IMessage[]
        {
            MessageEnvelope.Create(new UserChatMessage(question)),
            new TextMessage(Role.Assistant, question, from: "user"),
            new MultiModalMessage(Role.Assistant,
                [
                    new TextMessage(Role.Assistant, question, from: "user"),
                ],
                from: "user"),
        };

        foreach (var message in messages)
        {
            var reply = await functionCallAgent.SendAsync(message);

            reply.Should().BeOfType<ToolCallAggregateMessage>();
            reply.From.Should().Be("assistant");
            reply.GetToolCalls()!.Count().Should().Be(1);
            reply.GetToolCalls()!.First().FunctionName.Should().Be(this.GetWeatherAsyncFunctionContract.Name);
            reply.GetContent()!.ToLower().Should().Contain("seattle");
        }

        // test streaming
        foreach (var message in messages)
        {
            var reply = functionCallAgent.GenerateStreamingReplyAsync([message]);
            await foreach (var streamingMessage in reply)
            {
                if (streamingMessage is not IMessage)
                {
                    streamingMessage.Should().BeOfType<ToolCallMessageUpdate>();
                    streamingMessage.As<ToolCallMessageUpdate>().From.Should().Be("assistant");
                }
                else
                {
                    streamingMessage.Should().BeOfType<ToolCallAggregateMessage>();
                    streamingMessage.As<IMessage>().GetContent()!.ToLower().Should().Contain("seattle");
                }
            }
        }
    }

    [ApiKeyFact("AZURE_OPENAI_API_KEY", "AZURE_OPENAI_ENDPOINT", "AZURE_OPENAI_DEPLOY_NAME")]
    public async Task ItCreateOpenAIChatAgentWithChatCompletionOptionAsync()
    {
        var deployName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOY_NAME") ?? throw new Exception("Please set AZURE_OPENAI_DEPLOY_NAME environment variable.");
        var openaiClient = CreateOpenAIClientFromAzureOpenAI();
        var options = new ChatCompletionOptions()
        {
            Temperature = 0.7f,
            MaxTokens = 1,
        };

        var openAIChatAgent = new OpenAIChatAgent(
            chatClient: openaiClient.GetChatClient(deployName),
            name: "assistant",
            options: options)
            .RegisterMessageConnector();

        var respond = await openAIChatAgent.SendAsync("hello");
        respond.GetContent()?.Should().NotBeNullOrEmpty();
    }


    private OpenAIClient CreateOpenAIClientFromAzureOpenAI()
    {
        var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? throw new Exception("Please set AZURE_OPENAI_ENDPOINT environment variable.");
        var key = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ?? throw new Exception("Please set AZURE_OPENAI_API_KEY environment variable.");
        return new AzureOpenAIClient(new Uri(endpoint), new Azure.AzureKeyCredential(key));
    }
}
