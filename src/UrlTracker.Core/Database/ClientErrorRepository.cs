﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NPoco;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Persistence.Querying;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Persistence.Querying;
using Umbraco.Cms.Infrastructure.Persistence.Repositories.Implement;
using Umbraco.Cms.Infrastructure.Persistence.SqlSyntax;
using Umbraco.Cms.Infrastructure.Scoping;
using Umbraco.Extensions;
using UrlTracker.Core.Database.Dtos;
using UrlTracker.Core.Database.Entities;
using UrlTracker.Core.Database.Factories;
using UrlTracker.Core.Database.Models;

namespace UrlTracker.Core.Database
{
    [ExcludeFromCodeCoverage]
    public class ClientErrorRepository
        : EntityRepositoryBase<int, IClientError>, IClientErrorRepository
    {
        private const string _referrerTableAlias = "r";
        private const string _mostCommonReferrerTableAlias = "mcr";
        private const string _occurrancesTableAlias = "ref";

        public ClientErrorRepository(IScopeAccessor scopeAccessor,
                                     AppCaches appCaches,
                                     ILogger<EntityRepositoryBase<int, IClientError>> logger)
            : base(scopeAccessor, appCaches, logger)
        { }

        #region Old implementation
        public Task<IReadOnlyCollection<IClientError>> GetAsync(IEnumerable<string> urlsAndPaths, int? rootNodeId = null, string? culture = null)
        {
            IQuery<IClientError> query = SqlContext.Query<IClientError>().Where(e => urlsAndPaths.Contains(e.Url));
            var results = Get(query);
            return Task.FromResult<IReadOnlyCollection<IClientError>>(results.ToList());
        }

        public async Task<int> CountAsync(DateTime start, DateTime end)
        {
            var query = Sql().SelectCount()
                             .From<ClientError2ReferrerDto>()
                             .Where<ClientError2ReferrerDto>(e => e.CreateDate >= start && e.CreateDate <= end);

            return await Database.ExecuteScalarAsync<int>(query).ConfigureAwait(false);
        }

        public async Task<ClientErrorEntityCollection> GetAsync(uint skip, uint take, string? query, OrderBy order, bool descending)
        {
            var countQuery = Sql()
                .SelectCount()
                .From<ClientErrorDto>()
                .Where<ClientErrorDto>(e => e.Ignored == false);

            if (query is not null)
            {
                countQuery.Where<ClientErrorDto>(e => e.Url.Contains(query));
            }

            Task<int> totalRecordsTask = Database.ExecuteScalarAsync<int>(countQuery);

            var selectQuery = GetBaseQuery(isCount: false)
                .Where<ClientErrorDto>(e => e.Ignored == false);
            if (query is not null)
            {
                selectQuery.Where<ClientErrorDto>(e => e.Url.Contains(query));
            }
            string orderParameter = order switch
            {
                OrderBy.LastOccurrence or
                OrderBy.Created => SqlSyntax.GetQuotedColumnName(Defaults.DatabaseSchema.AggregateColumns.MostRecentOccurrence),
                OrderBy.Occurrences => SqlSyntax.GetQuotedColumnName(Defaults.DatabaseSchema.AggregateColumns.TotalOccurrences),
                _ => throw new ArgumentOutOfRangeException(nameof(order)),
            };
            selectQuery = selectQuery.GenericOrderBy(descending, orderParameter);
            var dtos = await Database.SkipTakeAsync<ExtendedClientErrorDto>(skip, take, selectQuery).ConfigureAwait(false);

            return ClientErrorEntityCollection.Create(dtos.Select(ClientErrorFactory.BuildEntity), await totalRecordsTask);
        }
        #endregion

        public override void Delete(IClientError entity)
        {
            base.Delete(entity);

            var deleteReferrersQuery = Sql().Delete()
                                            .From<ReferrerDto>()
                                            .WhereNotIn<ReferrerDto>(e => e.Id, Sql().Select<ClientError2ReferrerDto>(e => e.Referrer)
                                                                                     .From<ClientError2ReferrerDto>());

            Database.Execute(deleteReferrersQuery);
        }

        protected override IClientError? PerformGet(int id)
        {
            var sql = GetBaseQuery(false);
            sql.Where<ClientErrorDto>(e => e.Id == id);
            var dto = Database.Fetch<ExtendedClientErrorDto>(sql).FirstOrDefault();

            if (dto is null) return null;

            return ClientErrorFactory.BuildEntity(dto);
        }

        protected override IEnumerable<IClientError> PerformGetAll(params int[]? ids)
        {
            var sql = GetBaseQuery(false);

            if (ids?.Any() is true) sql.WhereIn<ClientErrorDto>(e => e.Id, ids);

            var dtos = Database.Fetch<ExtendedClientErrorDto>(sql);
            return dtos.Select(ClientErrorFactory.BuildEntity);
        }

        protected override IEnumerable<IClientError> PerformGetByQuery(IQuery<IClientError> query)
        {
            var sql = GetBaseQuery(false);

            var translator = new SqlTranslator<IClientError>(sql, query);
            sql = translator.Translate();

            var dtos = Database.Fetch<ExtendedClientErrorDto>(sql);
            return dtos.Select(ClientErrorFactory.BuildEntity);
        }

        protected override void PersistNewItem(IClientError entity)
        {
            entity.AddingEntity();
            if (entity.Key == Guid.Empty) entity.Key = Guid.NewGuid();

            var dto = ClientErrorFactory.BuildDto(entity);
            var id = Convert.ToInt32(Database.Insert(dto));

            entity.Id = id;
            entity.ResetDirtyProperties();
        }

        protected override void PersistUpdatedItem(IClientError entity)
        {
            entity.UpdatingEntity();

            var dto = ClientErrorFactory.BuildDto(entity);
            Database.Update(dto);

            entity.ResetDirtyProperties();
        }

        protected override Sql<ISqlContext> GetBaseQuery(bool isCount)
        {
            var sql = Sql();

            ISqlSyntaxProvider syntax = sql.SqlContext.SqlSyntax;
            sql = isCount
                ? sql.SelectCount()
                : sql.Select<ClientErrorDto>(e => e.Id, e => e.Key, e => e.Url, e => e.CreateDate, e => e.Ignored, e => e.Strategy)
                     .AndSelect($"{syntax.GetQuotedColumnName(_occurrancesTableAlias)}.{syntax.GetQuotedColumnName("count")} as {syntax.GetQuotedColumnName(Defaults.DatabaseSchema.AggregateColumns.TotalOccurrences)}")
                     .AndSelect($"{syntax.GetQuotedColumnName(_occurrancesTableAlias)}.{syntax.GetQuotedColumnName("mostRecentOccurrence")} as {syntax.GetQuotedColumnName(Defaults.DatabaseSchema.AggregateColumns.MostRecentOccurrence)}")
                     .AndSelect($"{syntax.GetQuotedColumnName(_referrerTableAlias)}.{syntax.GetQuotedColumnName("url")} as {syntax.GetQuotedColumnName(Defaults.DatabaseSchema.AggregateColumns.MostCommonReferrer)}");

            sql.From<ClientErrorDto>();
            var clientErrorGroups = SqlContext.Templates.Get("clientErrorGroups", sql
                => sql.SelectCount("count")
                      .AndSelect<ClientError2ReferrerDto>(e => e.ClientError)
                      .AndSelectMax<ClientError2ReferrerDto>("mostRecentOccurrence", null, e => e.CreateDate)
                      .From<ClientError2ReferrerDto>()
                      .GroupBy<ClientError2ReferrerDto>(e => e.ClientError));

            var mostCommonReferrers = SqlContext.Templates.Get("mostCommonReferrers", sql
                => sql.Select<ClientError2ReferrerDto>(_referrerTableAlias, e => e.ClientError, e => e.Referrer)
                      .From(_referrerTableAlias, s => s.Select<ClientError2ReferrerDto>(e => e.ClientError, e => e.Referrer)
                                    .Append(", ROW_NUMBER() over (PARTITION BY [clientError] ORDER BY COUNT([Referrer]) DESC) as rn")
                                    .From<ClientError2ReferrerDto>()
                                    .WhereNotNull<ClientError2ReferrerDto>(e => e.Referrer)
                                    .GroupBy<ClientError2ReferrerDto>(e => e.ClientError, e => e.Referrer))
                      .Where("rn = 1")
                      );

            sql.LeftJoin(clientErrorGroups.Sql(), _occurrancesTableAlias)
               .On<ClientErrorDto, ClientError2ReferrerDto>((l, r) => l.Id == r.ClientError, null, _occurrancesTableAlias)
               .LeftJoin(mostCommonReferrers.Sql(), _mostCommonReferrerTableAlias)
               .On<ClientErrorDto, ClientError2ReferrerDto>((l, r) => l.Id == r.ClientError, null, _mostCommonReferrerTableAlias)
               .LeftJoin<ReferrerDto>(_referrerTableAlias)
               .On<ClientError2ReferrerDto, ReferrerDto>((l, r) => l.Referrer == r.Id, _mostCommonReferrerTableAlias, _referrerTableAlias);
            return sql;
        }

        protected override string GetBaseWhereClause()
        {
            return $"{SqlSyntax.GetFieldName<ClientErrorDto>(e => e.Id)} = @id";
        }

        protected override IEnumerable<string> GetDeleteClauses()
        {
            var list = new List<string>
            {
                $"DELETE FROM {Defaults.DatabaseSchema.Tables.ClientError2Referrer} WHERE clientError = @id",
                $"DELETE FROM {Defaults.DatabaseSchema.Tables.ClientError} WHERE id = @id"
            };
            return list;
        }

        public void Report(IClientError clientError, DateTime moment, IReferrer? referrer)
        {
            var dto = new ClientError2ReferrerDto
            {
                ClientError = clientError.Id,
                CreateDate = moment,
                Referrer = referrer?.Id
            };

            Database.Insert(dto);
        }
    }
}
