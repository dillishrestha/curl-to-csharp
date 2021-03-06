﻿using System;
using System.Collections.Generic;
using System.Linq;

using CurlToCSharp.Extensions;
using CurlToCSharp.Models;
using CurlToCSharp.Models.Parsing;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http;
using Microsoft.Net.Http.Headers;

namespace CurlToCSharp.Services
{
    public class CommandLineParser : ICommandLineParser
    {
        private readonly IEnumerable<ParameterEvaluator> _evaluators;

        public CommandLineParser(ParsingOptions parsingOptions)
            : this(EvaluatorProvider.All(parsingOptions))
        {
        }

        private CommandLineParser(IEnumerable<ParameterEvaluator> evaluators)
        {
            _evaluators = evaluators;
        }

        public ConvertResult<CurlOptions> Parse(Span<char> commandLine)
        {
            if (commandLine.IsEmpty)
            {
                throw new ArgumentException("The command line is empty.", nameof(commandLine));
            }

            var parseResult = new ConvertResult<CurlOptions>(new CurlOptions());
            var parseState = new ParseState();
            while (!commandLine.IsEmpty)
            {
                commandLine = commandLine.Trim();
                if (commandLine.IsEmpty)
                {
                    break;
                }

                if (commandLine.IsParameter())
                {
                    var parameter = commandLine.ReadParameter();
                    EvaluateParameter(parameter, ref commandLine, parseResult);
                }
                else
                {
                    var value = commandLine.ReadValue();
                    EvaluateValue(parseResult, parseState, value);
                }
            }

            PostParsing(parseResult, parseState);

            return parseResult;
        }

        private static void EvaluateValue(ConvertResult<CurlOptions> convertResult, ParseState parseState, Span<char> value)
        {
            var valueString = value.ToString();
            if (string.Equals(valueString, "curl", StringComparison.InvariantCultureIgnoreCase))
            {
                parseState.IsCurlCommand = true;
            }
            else if (convertResult.Data.Url == null && Uri.TryCreate(valueString, UriKind.Absolute, out var url)
                                                  && !string.IsNullOrEmpty(url.Host))
            {
                convertResult.Data.Url = url;
            }
            else
            {
                parseState.LastUnknownValue = valueString;
            }
        }

        private void EvaluateParameter(Span<char> parameter, ref Span<char> commandLine, ConvertResult<CurlOptions> convertResult)
        {
            var par = parameter.ToString();

            foreach (var evaluator in _evaluators)
            {
                if (evaluator.CanEvaluate(par))
                {
                    evaluator.Evaluate(ref commandLine, convertResult);

                    return;
                }
            }

            convertResult.Warnings.Add($"Parameter \"{par}\" is not supported");
        }

        private void PostParsing(ConvertResult<CurlOptions> result, ParseState state)
        {
            if (result.Data.Url == null
                && !string.IsNullOrWhiteSpace(state.LastUnknownValue)
                && Uri.TryCreate($"http://{state.LastUnknownValue}", UriKind.Absolute, out Uri url))
            {
                result.Data.Url = url;
            }

            // This option overrides -F, --form and -I, --head and -T, --upload-file.
            if (result.Data.HasDataPayload)
            {
                result.Data.UploadFiles.Clear();
                result.Data.FormData.Clear();
            }

            if (result.Data.HasFormPayload)
            {
                result.Data.UploadFiles.Clear();
            }

            if (result.Data.HttpMethod == null)
            {
                if (result.Data.HasDataPayload)
                {
                    result.Data.HttpMethod = HttpMethod.Post.ToString()
                        .ToUpper();
                }
                else if (result.Data.HasFormPayload)
                {
                    result.Data.HttpMethod = HttpMethod.Post.ToString()
                        .ToUpper();
                }
                else if (result.Data.HasFilePayload)
                {
                    result.Data.HttpMethod = HttpMethod.Put.ToString()
                        .ToUpper();
                }
                else
                {
                    result.Data.HttpMethod = HttpMethod.Get.ToString()
                        .ToUpper();
                }
            }

            if (!result.Data.Headers.GetCommaSeparatedValues(HeaderNames.ContentType)
                    .Any() && result.Data.HasDataPayload)
            {
                result.Data.Headers.TryAdd(HeaderNames.ContentType, "application/x-www-form-urlencoded");
            }

            if (!state.IsCurlCommand)
            {
                result.Errors.Add("Invalid curl command");
            }

            if (result.Data.Url == null)
            {
                result.Errors.Add("Unable to parse URL");
            }
        }
    }
}
