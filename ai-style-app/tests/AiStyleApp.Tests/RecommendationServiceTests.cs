using AiStyleApp.Api.Models;
using AiStyleApp.Api.Services;
using AiStyleApp.Data;
using AiStyleApp.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AiStyleApp.Tests;

public class RecommendationServiceTests
{
    [Fact]
    public async Task CreateAndEnqueueAsync_CreatesPublishFirstRecommendationPost()
    {
        await using var db = CreateDbContext();

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        var request = new CreateRecommendationsRequest(
            ImageUrl: "https://example.com/photo.jpg",
            Gender: "female",
            Preferences: null);

        var (analysisJobId, recommendationPostId) = await service.CreateAndEnqueueAsync(request, "user-1");

        Assert.NotEqual(Guid.Empty, analysisJobId);
        Assert.NotEqual(Guid.Empty, recommendationPostId);

        var post = await db.StyleItems.FirstOrDefaultAsync(x => x.Id == recommendationPostId);
        Assert.NotNull(post);
        Assert.True(post!.IsResultPublic);
        Assert.Contains(analysisJobId.ToString(), post.Description, StringComparison.Ordinal);

        var analysisJob = await db.FaceAnalysisJobs.SingleAsync(x => x.Id == analysisJobId);
        Assert.Equal(recommendationPostId, analysisJob.PrimaryStyleItemId);
        Assert.Null(analysisJob.PrimaryStyleId);
        Assert.Null(analysisJob.PrimaryGenerationJobId);
    }

    [Fact]
    public async Task SubmitRatingsAsync_PersistsOneFeedbackEntryPerRanking()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded"
        };

        db.FaceAnalysisJobs.Add(analysisJob);
        await db.SaveChangesAsync();

        var firstGenerationJobId = Guid.NewGuid();
        var secondGenerationJobId = Guid.NewGuid();
        db.RecommendationExposures.Add(new RecommendationExposureEntity
        {
            AnalysisJobId = analysisJob.Id,
            UserId = analysisJob.UserId,
            ExperimentVersion = "controlled-exploration-v1",
            ExperimentApplied = true,
            PrimaryStyleId = "short-quiff",
            PrimaryGenerationJobId = firstGenerationJobId,
            Candidates =
            [
                new RecommendationExposureCandidateEntity
                {
                    StyleId = "short-quiff",
                    StyleName = "Short Quiff",
                    RecommendationRank = 1,
                    RankingScore = 0.9,
                    IsPrimary = true,
                    WasShown = true,
                    ShownOrder = 1,
                    SelectionProbability = 1,
                    GenerationJobId = firstGenerationJobId
                },
                new RecommendationExposureCandidateEntity
                {
                    StyleId = "classic-side-part",
                    StyleName = "Classic Side Part",
                    RecommendationRank = 2,
                    RankingScore = 0.8,
                    WasShown = true,
                    ShownOrder = 2,
                    SelectionProbability = 1,
                    GenerationJobId = secondGenerationJobId
                }
            ]
        });
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        var request = new SubmitRecommendationRatingsRequest(
            AnalysisJobId: analysisJob.Id,
            Rankings: new[]
            {
                new RecommendationRankingInput(firstGenerationJobId, 1),
                new RecommendationRankingInput(secondGenerationJobId, 2)
            },
            FeedbackTags: new[] { "greatMatch" },
            Comment: "top 2 look strong");

        await service.SubmitRatingsAsync(analysisJob.Id, request, "user-1");

        var saved = db.RecommendationFeedback
            .Where(x => x.AnalysisJobId == analysisJob.Id)
            .OrderBy(x => x.CreatedAtUtc)
            .ToList();

        Assert.Equal(2, saved.Count);
        Assert.Equal(1, saved[0].Rating);
        Assert.Equal(2, saved[1].Rating);
        Assert.Equal("top 2 look strong", saved[0].Comment);
    }

    [Fact]
    public async Task SubmitRatingsAsync_UnshownGenerationJob_ThrowsWithoutPersistingFeedback()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded"
        };
        db.FaceAnalysisJobs.Add(analysisJob);
        await db.SaveChangesAsync();

        var shownGenerationJobId = Guid.NewGuid();
        db.RecommendationExposures.Add(new RecommendationExposureEntity
        {
            AnalysisJobId = analysisJob.Id,
            UserId = analysisJob.UserId,
            ExperimentVersion = "controlled-exploration-v1",
            ExperimentApplied = true,
            PrimaryStyleId = "short-quiff",
            PrimaryGenerationJobId = shownGenerationJobId,
            Candidates =
            [
                new RecommendationExposureCandidateEntity
                {
                    StyleId = "short-quiff",
                    StyleName = "Short Quiff",
                    RecommendationRank = 1,
                    RankingScore = 0.9,
                    IsPrimary = true,
                    WasShown = true,
                    ShownOrder = 1,
                    SelectionProbability = 1,
                    GenerationJobId = shownGenerationJobId
                }
            ]
        });
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);
        var request = new SubmitRecommendationRatingsRequest(
            AnalysisJobId: analysisJob.Id,
            Rankings: [new RecommendationRankingInput(Guid.NewGuid(), 1)],
            FeedbackTags: null,
            Comment: null);

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => service.SubmitRatingsAsync(analysisJob.Id, request, analysisJob.UserId));

        Assert.Contains("shown candidate set", error.Message, StringComparison.Ordinal);
        Assert.Empty(db.RecommendationFeedback);
    }

    [Fact]
    public async Task SubmitRatingsAsync_InvalidRank_ThrowsArgumentException()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded"
        };

        db.FaceAnalysisJobs.Add(analysisJob);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        var request = new SubmitRecommendationRatingsRequest(
            AnalysisJobId: analysisJob.Id,
            Rankings: new[]
            {
                new RecommendationRankingInput(Guid.NewGuid(), 4)
            },
            FeedbackTags: null,
            Comment: null);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitRatingsAsync(analysisJob.Id, request, "user-1"));
    }

    [Fact]
    public async Task SubmitRatingsAsync_DuplicateGenerationJobId_ThrowsArgumentException()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded"
        };

        db.FaceAnalysisJobs.Add(analysisJob);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        var duplicateJobId = Guid.NewGuid();
        var request = new SubmitRecommendationRatingsRequest(
            AnalysisJobId: analysisJob.Id,
            Rankings: new[]
            {
                new RecommendationRankingInput(duplicateJobId, 1),
                new RecommendationRankingInput(duplicateJobId, 2)
            },
            FeedbackTags: null,
            Comment: null);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitRatingsAsync(analysisJob.Id, request, "user-1"));
    }

    [Fact]
    public async Task SubmitRatingsAsync_DuplicateRank_ThrowsArgumentException()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded"
        };

        db.FaceAnalysisJobs.Add(analysisJob);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        var request = new SubmitRecommendationRatingsRequest(
            AnalysisJobId: analysisJob.Id,
            Rankings: new[]
            {
                new RecommendationRankingInput(Guid.NewGuid(), 1),
                new RecommendationRankingInput(Guid.NewGuid(), 1)
            },
            FeedbackTags: null,
            Comment: null);

        await Assert.ThrowsAsync<ArgumentException>(() => service.SubmitRatingsAsync(analysisJob.Id, request, "user-1"));
    }

    [Fact]
    public async Task GetStatusAsync_PopulatesBestAndExperimentalVariants_FromLinkedStyleItems()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded",
            QualityPassed = true,
            AnalysisConfidence = 0.82,
            RecommendationsJson = "[{\"styleId\":\"textured-crop\",\"styleName\":\"Textured Crop\",\"score\":0.94,\"reasons\":[],\"constraints\":[]}]",
            FeatureVectorJson = "{\"faceShape\":\"Square\"}"
        };

        db.FaceAnalysisJobs.Add(analysisJob);

        var primaryItem = new StyleItemEntity
        {
            UserId = "user-1",
            Name = "Recommended: Textured Crop",
            Description = $"Primary recommendation from analysis job {analysisJob.Id}",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            IsResultPublic = true
        };

        var primaryJob = new StyleJobEntity
        {
            StyleItemId = primaryItem.Id,
            UserId = "user-1",
            JobType = "generate-style",
            Status = "Succeeded",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            ResultImageUrl = "https://example.com/best.jpg"
        };
        primaryItem.Jobs.Add(primaryJob);

        var experimentalItem = new StyleItemEntity
        {
            UserId = "user-1",
            Name = "Experimental 1: Classic Side Part",
            Description = $"Experimental recommendation from analysis job {analysisJob.Id}",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            IsResultPublic = false
        };

        var experimentalJob = new StyleJobEntity
        {
            StyleItemId = experimentalItem.Id,
            UserId = "user-1",
            JobType = "generate-style",
            Status = "Processing",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl
        };
        experimentalItem.Jobs.Add(experimentalJob);

        analysisJob.PrimaryStyleId = "textured-crop";
        analysisJob.PrimaryStyleItemId = primaryItem.Id;
        analysisJob.PrimaryGenerationJobId = primaryJob.Id;

        db.StyleItems.AddRange(primaryItem, experimentalItem);
        db.RecommendationFeedback.Add(new RecommendationFeedbackEntity
        {
            AnalysisJobId = analysisJob.Id,
            UserId = "user-1",
            SelectedStyleId = experimentalJob.Id.ToString(),
            Rating = 1
        });

        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Features:ExperimentationModeEnabled"] = "true",
            ["Features:ExperimentationTrafficPercent"] = "100"
        }).Build();

        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);
        var result = await service.GetStatusAsync(analysisJob.Id, "user-1");

        Assert.NotNull(result);
        Assert.Equal(primaryItem.Id, result!.RecommendationPostId);
        Assert.Equal("Published", result.PublishStatus);
        Assert.Equal("Square", result.AnalysisSummary.FaceShape);
        Assert.NotNull(result.BestVariant);
        Assert.Equal(primaryJob.Id, result.BestVariant!.GenerationJobId);
        Assert.Equal(primaryJob.Id, result.PrimaryGenerationJobId);
        Assert.Single(result.ExperimentalVariants);
        Assert.Equal(experimentalJob.Id, result.ExperimentalVariants[0].GenerationJobId);
        Assert.Equal("1", result.ExperimentalVariants[0].SelectedRank);
    }

    [Fact]
    public async Task GetStatusAsync_ExplicitPrimaryLinkage_WinsOverPublicItemOrder()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Status = "Succeeded",
            QualityPassed = true,
            RecommendationsJson = """
                [
                  {"styleId":"classic-side-part","styleName":"Classic Side Part","score":0.90,"reasons":[],"constraints":[]},
                  {"styleId":"textured-crop","styleName":"Textured Crop","score":0.94,"reasons":[],"constraints":[]}
                ]
                """
        };

        var misleadingItem = CreateLinkedStyleItem(
            analysisJob,
            "Misleading Public Item",
            "https://example.com/not-primary.jpg",
            DateTimeOffset.UtcNow.AddMinutes(-1));
        var explicitPrimaryItem = CreateLinkedStyleItem(
            analysisJob,
            "Explicit Primary Item",
            "https://example.com/primary.jpg",
            DateTimeOffset.UtcNow);
        var explicitPrimaryJob = explicitPrimaryItem.Jobs.Single();

        analysisJob.PrimaryStyleId = "textured-crop";
        analysisJob.PrimaryStyleItemId = explicitPrimaryItem.Id;
        analysisJob.PrimaryGenerationJobId = explicitPrimaryJob.Id;

        db.FaceAnalysisJobs.Add(analysisJob);
        db.StyleItems.AddRange(misleadingItem, explicitPrimaryItem);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        var result = await service.GetStatusAsync(analysisJob.Id, analysisJob.UserId);

        Assert.NotNull(result);
        Assert.Equal(explicitPrimaryItem.Id, result!.RecommendationPostId);
        Assert.Equal("textured-crop", result.BestRecommendation?.StyleId);
        Assert.Equal(explicitPrimaryJob.Id, result.BestVariant?.GenerationJobId);
        Assert.Equal("textured-crop", result.PrimaryStyleId);
        Assert.Equal(explicitPrimaryJob.Id, result.PrimaryGenerationJobId);
    }

    [Fact]
    public async Task GetStatusAsync_DifferentOwner_ReturnsNull()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Status = "Succeeded"
        };
        db.FaceAnalysisJobs.Add(analysisJob);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        var result = await service.GetStatusAsync(analysisJob.Id, "user-2");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetStatusAsync_DuplicateFeedbackForSameSelection_UsesLatestEntry()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded",
            QualityPassed = true,
            AnalysisConfidence = 0.82,
            RecommendationsJson = "[]",
            FeatureVectorJson = "{\"faceShape\":\"Square\"}"
        };

        db.FaceAnalysisJobs.Add(analysisJob);

        var primaryItem = new StyleItemEntity
        {
            UserId = "user-1",
            Name = "Recommended",
            Description = $"Primary recommendation from analysis job {analysisJob.Id}",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            IsResultPublic = true
        };
        primaryItem.Jobs.Add(new StyleJobEntity
        {
            StyleItemId = primaryItem.Id,
            UserId = "user-1",
            JobType = "generate-style",
            Status = "Succeeded",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            ResultImageUrl = "https://example.com/best.jpg"
        });

        var experimentalItem = new StyleItemEntity
        {
            UserId = "user-1",
            Name = "Experimental",
            Description = $"Experimental recommendation from analysis job {analysisJob.Id}",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            IsResultPublic = false
        };

        var experimentalJob = new StyleJobEntity
        {
            StyleItemId = experimentalItem.Id,
            UserId = "user-1",
            JobType = "generate-style",
            Status = "Succeeded",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            ResultImageUrl = "https://example.com/exp.jpg"
        };
        experimentalItem.Jobs.Add(experimentalJob);

        db.StyleItems.AddRange(primaryItem, experimentalItem);
        db.RecommendationFeedback.AddRange(
            new RecommendationFeedbackEntity
            {
                AnalysisJobId = analysisJob.Id,
                UserId = "user-1",
                SelectedStyleId = experimentalJob.Id.ToString(),
                Rating = 1,
                CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
            },
            new RecommendationFeedbackEntity
            {
                AnalysisJobId = analysisJob.Id,
                UserId = "user-1",
                SelectedStyleId = experimentalJob.Id.ToString(),
                Rating = 2,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });

        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Features:ExperimentationModeEnabled"] = "true",
            ["Features:ExperimentationTrafficPercent"] = "100"
        }).Build();

        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);
        var result = await service.GetStatusAsync(analysisJob.Id, "user-1");

        Assert.NotNull(result);
        Assert.Single(result!.ExperimentalVariants);
        Assert.Equal("2", result.ExperimentalVariants[0].SelectedRank);
    }

    [Fact]
    public async Task GetStatusAsync_UsesExperimentMetadataFromFeatureVector_WhenPresent()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded",
            QualityPassed = true,
            AnalysisConfidence = 0.82,
            RecommendationsJson = "[]",
            FeatureVectorJson = """
            {
              "experiment": {
                "enabled": true,
                "trafficPercent": 25,
                "applied": false,
                "bucketKey": "user-hash-91"
              }
            }
            """
        };

        db.FaceAnalysisJobs.Add(analysisJob);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Features:ExperimentationModeEnabled"] = "true",
            ["Features:ExperimentationTrafficPercent"] = "100"
        }).Build();

        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        var result = await service.GetStatusAsync(analysisJob.Id, "user-1");

        Assert.NotNull(result);
        Assert.NotNull(result!.Experiment);
        Assert.True(result.Experiment!.Enabled);
        Assert.Equal(25, result.Experiment.TrafficPercent);
        Assert.False(result.Experiment.Applied);
        Assert.Equal("user-hash-91", result.Experiment.BucketKey);
    }

    [Fact]
    public async Task GetStatusAsync_FallsBackToConfigExperimentMetadata_WhenFeatureVectorHasNoExperiment()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded",
            QualityPassed = true,
            AnalysisConfidence = 0.82,
            RecommendationsJson = "[]",
            FeatureVectorJson = "{\"source\":\"worker-v1\"}"
        };

        db.FaceAnalysisJobs.Add(analysisJob);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Features:ExperimentationModeEnabled"] = "true",
            ["Features:ExperimentationTrafficPercent"] = "100"
        }).Build();

        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        var result = await service.GetStatusAsync(analysisJob.Id, "user-1");

        Assert.NotNull(result);
        Assert.NotNull(result!.Experiment);
        Assert.True(result.Experiment!.Enabled);
        Assert.Equal(100, result.Experiment.TrafficPercent);
        Assert.True(result.Experiment.Applied);
        Assert.Equal("user-hash-42", result.Experiment.BucketKey);
    }

    [Fact]
    public async Task GetStatusAsync_MalformedFeatureVector_ReturnsNullDebugTelemetry()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded",
            QualityPassed = true,
            AnalysisConfidence = 0.82,
            RecommendationsJson = "[]",
            FeatureVectorJson = "[1,2,3]"
        };

        db.FaceAnalysisJobs.Add(analysisJob);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Features:ExperimentationModeEnabled"] = "true",
            ["Features:ExperimentationTrafficPercent"] = "100"
        }).Build();

        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        var result = await service.GetStatusAsync(analysisJob.Id, "user-1");

        Assert.NotNull(result);
        Assert.Null(result!.DebugTelemetry);
        Assert.Equal("Succeeded", result.Status);
    }

    [Fact]
    public async Task GetStatusAsync_AllVariantsTerminalWithoutSuccess_ReturnsGenerationFailureSignal()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded",
            QualityPassed = true,
            RecommendationsJson = "[]"
        };

        db.FaceAnalysisJobs.Add(analysisJob);

        var primaryItem = new StyleItemEntity
        {
            UserId = "user-1",
            Name = "Recommended",
            Description = $"Primary recommendation from analysis job {analysisJob.Id}",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            IsResultPublic = true
        };
        primaryItem.Jobs.Add(new StyleJobEntity
        {
            StyleItemId = primaryItem.Id,
            UserId = "user-1",
            JobType = "generate-style",
            Status = "Failed",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl
        });

        var experimentalItem = new StyleItemEntity
        {
            UserId = "user-1",
            Name = "Experimental",
            Description = $"Experimental recommendation from analysis job {analysisJob.Id}",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            IsResultPublic = false
        };
        experimentalItem.Jobs.Add(new StyleJobEntity
        {
            StyleItemId = experimentalItem.Id,
            UserId = "user-1",
            JobType = "generate-style",
            Status = "TimedOut",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl
        });

        db.StyleItems.AddRange(primaryItem, experimentalItem);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        var result = await service.GetStatusAsync(analysisJob.Id, "user-1");

        Assert.NotNull(result);
        Assert.Equal("GENERATION_ALL_VARIANTS_FAILED", result!.ErrorCode);
        Assert.Contains("without any successful variants", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStatusAsync_WithAnySucceededVariant_DoesNotReturnGenerationFailureSignal()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded",
            QualityPassed = true,
            RecommendationsJson = "[]"
        };

        db.FaceAnalysisJobs.Add(analysisJob);

        var primaryItem = new StyleItemEntity
        {
            UserId = "user-1",
            Name = "Recommended",
            Description = $"Primary recommendation from analysis job {analysisJob.Id}",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            IsResultPublic = true
        };
        primaryItem.Jobs.Add(new StyleJobEntity
        {
            StyleItemId = primaryItem.Id,
            UserId = "user-1",
            JobType = "generate-style",
            Status = "Succeeded",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            ResultImageUrl = "https://example.com/final.jpg"
        });

        db.StyleItems.Add(primaryItem);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        var result = await service.GetStatusAsync(analysisJob.Id, "user-1");

        Assert.NotNull(result);
        Assert.Null(result!.ErrorCode);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task GetStatusAsync_PrimaryFailedButExperimentSucceeded_ReturnsActionablePrimaryFailure()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Status = JobStatus.Succeeded,
            QualityPassed = true,
            RecommendationsJson = "[]"
        };

        var primaryItem = CreateLinkedStyleItem(
            analysisJob,
            "Primary",
            "https://example.com/primary.jpg",
            DateTimeOffset.UtcNow.AddMinutes(-1));
        var primaryJob = primaryItem.Jobs.Single();
        primaryJob.Status = JobStatus.Failed;
        primaryJob.ResultImageUrl = null;

        var experimentalItem = CreateLinkedStyleItem(
            analysisJob,
            "Experimental",
            "https://example.com/experimental.jpg",
            DateTimeOffset.UtcNow);
        var experimentalJob = experimentalItem.Jobs.Single();

        analysisJob.PrimaryStyleItemId = primaryItem.Id;
        analysisJob.PrimaryGenerationJobId = primaryJob.Id;
        db.FaceAnalysisJobs.Add(analysisJob);
        db.StyleItems.AddRange(primaryItem, experimentalItem);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        var result = await service.GetStatusAsync(analysisJob.Id, analysisJob.UserId);

        Assert.NotNull(result);
        Assert.Equal("PRIMARY_GENERATION_FAILED", result!.ErrorCode);
        Assert.Contains("automatic primary result", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(primaryJob.Id, result.BestVariant?.GenerationJobId);
        Assert.Equal(primaryJob.Id, result.PrimaryGenerationJobId);
        Assert.Equal(experimentalJob.Id, Assert.Single(result.ExperimentalVariants).GenerationJobId);
    }

    [Fact]
    public async Task GetStatusAsync_LegacySelection_DoesNotSuppressGenerationFailureSignal()
    {
        await using var db = CreateDbContext();
        var selectedJobId = Guid.NewGuid();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded",
            QualityPassed = true,
            RecommendationsJson = "[]",
            SelectedGenerationJobId = selectedJobId,
            SelectedAtUtc = DateTimeOffset.UtcNow
        };

        db.FaceAnalysisJobs.Add(analysisJob);

        var primaryItem = new StyleItemEntity
        {
            UserId = "user-1",
            Name = "Recommended",
            Description = $"Primary recommendation from analysis job {analysisJob.Id}",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            IsResultPublic = true
        };
        primaryItem.Jobs.Add(new StyleJobEntity
        {
            Id = selectedJobId,
            StyleItemId = primaryItem.Id,
            UserId = "user-1",
            JobType = "generate-style",
            Status = "Failed",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl
        });

        db.StyleItems.Add(primaryItem);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        var result = await service.GetStatusAsync(analysisJob.Id, "user-1");

        Assert.NotNull(result);
        Assert.Equal("GENERATION_ALL_VARIANTS_FAILED", result!.ErrorCode);
        Assert.Contains("without any successful variants", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FinalizeSelectionAsync_PersistsSelectedGenerationJobId()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded"
        };

        db.FaceAnalysisJobs.Add(analysisJob);

        var styleItem = new StyleItemEntity
        {
            UserId = "user-1",
            Name = "Recommended",
            Description = $"Primary recommendation from analysis job {analysisJob.Id}",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            IsResultPublic = true
        };

        var styleJob = new StyleJobEntity
        {
            StyleItemId = styleItem.Id,
            UserId = "user-1",
            JobType = "generate-style",
            Status = "Succeeded",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            ResultImageUrl = "https://example.com/final.jpg"
        };
        styleItem.Jobs.Add(styleJob);

        db.StyleItems.Add(styleItem);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        var result = await service.FinalizeSelectionAsync(
            analysisJob.Id,
            new FinalizeRecommendationRequest(styleJob.Id),
            "user-1");

        Assert.Equal(analysisJob.Id, result.AnalysisJobId);
        Assert.Equal(styleJob.Id, result.SelectedGenerationJobId);
        Assert.False(result.AlreadyFinalized);

        var persisted = await db.FaceAnalysisJobs.FirstAsync(x => x.Id == analysisJob.Id);
        Assert.Equal(styleJob.Id, persisted.SelectedGenerationJobId);
        Assert.NotNull(persisted.SelectedAtUtc);
    }

    [Fact]
    public async Task FinalizeSelectionAsync_SameSelectionTwice_IsIdempotent()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded"
        };

        db.FaceAnalysisJobs.Add(analysisJob);

        var styleItem = new StyleItemEntity
        {
            UserId = "user-1",
            Name = "Recommended",
            Description = $"Primary recommendation from analysis job {analysisJob.Id}",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            IsResultPublic = true
        };

        var styleJob = new StyleJobEntity
        {
            StyleItemId = styleItem.Id,
            UserId = "user-1",
            JobType = "generate-style",
            Status = "Succeeded",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            ResultImageUrl = "https://example.com/final.jpg"
        };
        styleItem.Jobs.Add(styleJob);

        db.StyleItems.Add(styleItem);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        var first = await service.FinalizeSelectionAsync(
            analysisJob.Id,
            new FinalizeRecommendationRequest(styleJob.Id),
            "user-1");

        var second = await service.FinalizeSelectionAsync(
            analysisJob.Id,
            new FinalizeRecommendationRequest(styleJob.Id),
            "user-1");

        Assert.False(first.AlreadyFinalized);
        Assert.True(second.AlreadyFinalized);
        Assert.Equal(first.SelectedGenerationJobId, second.SelectedGenerationJobId);
    }

    [Fact]
    public async Task FinalizeSelectionAsync_WhenJobNotLinkedToAnalysis_ThrowsArgumentException()
    {
        await using var db = CreateDbContext();
        var analysisJob = new FaceAnalysisJobEntity
        {
            UserId = "user-1",
            ImageUrl = "https://example.com/photo.jpg",
            Gender = "female",
            Status = "Succeeded"
        };

        db.FaceAnalysisJobs.Add(analysisJob);

        var unrelatedItem = new StyleItemEntity
        {
            UserId = "user-1",
            Name = "Unrelated",
            Description = "Unrelated recommendation post",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            IsResultPublic = true
        };

        var unrelatedJob = new StyleJobEntity
        {
            StyleItemId = unrelatedItem.Id,
            UserId = "user-1",
            JobType = "generate-style",
            Status = "Succeeded",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            ResultImageUrl = "https://example.com/unrelated.jpg"
        };
        unrelatedItem.Jobs.Add(unrelatedJob);

        db.StyleItems.Add(unrelatedItem);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var service = new RecommendationService(db, new StubQueuePublisher(), new StubMetricsLogger(), config);

        await Assert.ThrowsAsync<ArgumentException>(() => service.FinalizeSelectionAsync(
            analysisJob.Id,
            new FinalizeRecommendationRequest(unrelatedJob.Id),
            "user-1"));
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }

    private static StyleItemEntity CreateLinkedStyleItem(
        FaceAnalysisJobEntity analysisJob,
        string name,
        string resultImageUrl,
        DateTimeOffset createdAtUtc)
    {
        var item = new StyleItemEntity
        {
            UserId = analysisJob.UserId,
            Name = name,
            Description = $"Recommendation from analysis job {analysisJob.Id}",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            IsResultPublic = true,
            CreatedAtUtc = createdAtUtc
        };
        item.Jobs.Add(new StyleJobEntity
        {
            StyleItemId = item.Id,
            UserId = analysisJob.UserId,
            JobType = "generate-style",
            Status = "Succeeded",
            Prompt = "p",
            ImageUrl = analysisJob.ImageUrl,
            ResultImageUrl = resultImageUrl,
            CreatedAtUtc = createdAtUtc
        });

        return item;
    }

    private sealed class StubQueuePublisher : IQueuePublisher
    {
        public Task PublishAsync<T>(T message, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class StubMetricsLogger : IMetricsLogger
    {
        public void LogAnalysisJobCompleted(Guid jobId, string userId, bool qualityPassed, string? qualityFailureCode, double? analysisConfidence, string? faceShape, int recommendationCount, TimeSpan duration) { }
        public void LogAnalysisJobFailed(Guid jobId, string userId, string errorCode, string errorMessage, TimeSpan duration) { }
        public void LogRecommendationFeedbackSubmitted(Guid jobId, string userId, string? selectedStyleId, int? rating, string? tags, int? recommendationRank) { }
    }
}
