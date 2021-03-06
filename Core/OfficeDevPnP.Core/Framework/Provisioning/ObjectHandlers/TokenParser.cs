﻿using Microsoft.Online.SharePoint.TenantAdministration;
using Microsoft.SharePoint.Client;
using Microsoft.SharePoint.Client.Taxonomy;
using Newtonsoft.Json;
using OfficeDevPnP.Core.ALM;
using OfficeDevPnP.Core.Framework.Provisioning.Connectors;
using OfficeDevPnP.Core.Framework.Provisioning.Model;
using OfficeDevPnP.Core.Framework.Provisioning.ObjectHandlers.TokenDefinitions;
using OfficeDevPnP.Core.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Text.RegularExpressions;

namespace OfficeDevPnP.Core.Framework.Provisioning.ObjectHandlers
{
    /// <summary>
    /// Handles methods for token parser
    /// </summary>
    public class TokenParser
    {
        public Web _web;

        private List<TokenDefinition> _tokens;

        private bool _initializedFromHierarchy;

        /// <summary>
        /// List of token definitions
        /// </summary>
        public List<TokenDefinition> Tokens
        {
            get { return _tokens; }
            private set
            {
                _tokens = value;
            }
        }

        /// <summary>
        /// adds token definition
        /// </summary>
        /// <param name="tokenDefinition">A TokenDefinition object</param>
        public void AddToken(TokenDefinition tokenDefinition)
        {

            _tokens.Add(tokenDefinition);
            // ORDER IS IMPORTANT!
            var sortedTokens = from t in _tokens
                               orderby t.GetTokenLength() descending
                               select t;

            _tokens = sortedTokens.ToList();
            BuildTokenCache();
        }

        // Lightweight rebase
        public void Rebase(Web web)
        {
            foreach (var token in _tokens)
            {
                token.ClearCache();
            }
        }
        // Heavy rebase for switching templates
        public void Rebase(Web web, ProvisioningTemplate template)
        {
            _web = web;

            foreach (var token in _tokens.Where(t => t is VolatileTokenDefinition))
            {
                ((VolatileTokenDefinition)token).ClearVolatileCache(web);
            }
            _tokens.RemoveAll(t => t is SiteToken);

            _tokens.Add(new SiteToken(web));

            // remove list tokens
            AddListTokens(web); // tokens are remove in method
            // remove content type tokens
            AddContentTypeTokens(web);
            // remove field tokens
            _tokens.RemoveAll(t => t is FieldTitleToken || t is FieldIdToken);
            AddFieldTokens(web);
            // remove group tokens
            _tokens.RemoveAll(t => t is GroupIdToken || t is AssociatedGroupToken);
            AddGroupTokens(web);
            // remove role definition tokens
            _tokens.RemoveAll(t => t is RoleDefinitionToken || t is RoleDefinitionIdToken);
            AddRoleDefinitionTokens(web);
        }

        public TokenParser(Tenant tenant, Model.ProvisioningHierarchy hierarchy) :
            this(tenant, hierarchy, null)
        {
        }

        public TokenParser(Tenant tenant, Model.ProvisioningHierarchy hierarchy, ProvisioningTemplateApplyingInformation applyingInformation)
        {
            var web = ((ClientContext)tenant.Context).Web;
            web.EnsureProperties(w => w.ServerRelativeUrl, w => w.Url, w => w.Language);
            _web = web;
            _tokens = new List<TokenDefinition>();
            foreach (var parameter in hierarchy.Parameters)
            {
                _tokens.Add(new ParameterToken(null, parameter.Key, parameter.Value ?? string.Empty));
            }
            _tokens.Add(new GuidToken(null));
            _tokens.Add(new CurrentUserIdToken(web));
            _tokens.Add(new CurrentUserLoginNameToken(web));
            _tokens.Add(new CurrentUserFullNameToken(web));
            _tokens.Add(new AuthenticationRealmToken(web));
            _tokens.Add(new HostUrlToken(web));
            AddResourceTokens(web, hierarchy.Localizations, hierarchy.Connector);
            _initializedFromHierarchy = true;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="web">A SharePoint site or subsite</param>
        /// <param name="template">a provisioning template</param>
        public TokenParser(Web web, ProvisioningTemplate template) :
            this(web, template, null)
        {
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="web">A SharePoint site or subsite</param>
        /// <param name="template">a provisioning template</param>
        /// <param name="applyingInformation">The provisioning template applying information</param>
        public TokenParser(Web web, ProvisioningTemplate template, ProvisioningTemplateApplyingInformation applyingInformation)
        {
            web.EnsureProperties(w => w.ServerRelativeUrl, w => w.Url, w => w.Language);

            _web = web;

            _tokens = new List<TokenDefinition>();

            _tokens.Add(new SiteCollectionToken(web));
            _tokens.Add(new SiteCollectionIdToken(web));
            _tokens.Add(new SiteCollectionIdEncodedToken(web));
            _tokens.Add(new SiteToken(web));
            _tokens.Add(new MasterPageCatalogToken(web));
            _tokens.Add(new SiteCollectionTermStoreIdToken(web));
            _tokens.Add(new KeywordsTermStoreIdToken(web));
            _tokens.Add(new ThemeCatalogToken(web));
            _tokens.Add(new WebNameToken(web));
            _tokens.Add(new SiteIdToken(web));
            _tokens.Add(new SiteIdEncodedToken(web));
            _tokens.Add(new SiteOwnerToken(web));
            _tokens.Add(new SiteTitleToken(web));
            _tokens.Add(new AssociatedGroupIdToken(web, AssociatedGroupIdToken.AssociatedGroupType.owners));
            _tokens.Add(new AssociatedGroupIdToken(web, AssociatedGroupIdToken.AssociatedGroupType.members));
            _tokens.Add(new AssociatedGroupIdToken(web, AssociatedGroupIdToken.AssociatedGroupType.visitors));
            _tokens.Add(new AssociatedGroupToken(web, AssociatedGroupToken.AssociatedGroupType.owners));
            _tokens.Add(new AssociatedGroupToken(web, AssociatedGroupToken.AssociatedGroupType.members));
            _tokens.Add(new AssociatedGroupToken(web, AssociatedGroupToken.AssociatedGroupType.visitors));
            _tokens.Add(new GuidToken(web));
            _tokens.Add(new DateNowToken(web));
            _tokens.Add(new CurrentUserIdToken(web));
            _tokens.Add(new CurrentUserLoginNameToken(web));
            _tokens.Add(new CurrentUserFullNameToken(web));
            _tokens.Add(new AuthenticationRealmToken(web));
            _tokens.Add(new HostUrlToken(web));
#if !ONPREMISES
            _tokens.Add(new SiteCollectionConnectedOffice365GroupId(web));
            _tokens.Add(new EveryoneToken(web));
            _tokens.Add(new EveryoneButExternalUsersToken(web));
#endif

            AddListTokens(web);
            AddContentTypeTokens(web);

            if (!_initializedFromHierarchy)
            {
                // Add parameters
                foreach (var parameter in template.Parameters)
                {
                    _tokens.Add(new ParameterToken(web, parameter.Key, parameter.Value ?? string.Empty));
                }
            }


#if !ONPREMISES
            AddSiteDesignTokens(web, applyingInformation);
            AddSiteScriptTokens(web, applyingInformation);
            AddStorageEntityTokens(web);
#endif
            // Fields
            AddFieldTokens(web);

            // Handle resources
            AddResourceTokens(web, template.Localizations, template.Connector);

            // OOTB Roledefs
            AddRoleDefinitionTokens(web);

            // Groups
            AddGroupTokens(web);

            // AppPackages tokens
#if !ONPREMISES
            AddAppPackagesTokens(web);
#endif

            // TermStore related tokens
            AddTermStoreTokens(web);

            var sortedTokens = from t in _tokens
                               orderby t.GetTokenLength() descending
                               select t;

            _tokens = sortedTokens.ToList();
        }

        private void AddResourceTokens(Web web, LocalizationCollection localizations, FileConnectorBase connector)
        {
            if (localizations != null && localizations.Any())
            {
                // Read all resource keys in a list
                List<Tuple<string, uint, string>> resourceEntries = new List<Tuple<string, uint, string>>();
                foreach (var localizationEntry in localizations)
                {
                    var filePath = localizationEntry.ResourceFile;
                    using (var stream = connector.GetFileStream(filePath))
                    {
                        if (stream != null)
                        {
#if !NETSTANDARD2_0
                            using (ResXResourceReader resxReader = new ResXResourceReader(stream))
#else
                            using (ResourceReader resxReader = new ResourceReader(stream))
#endif
                            {
                                foreach (DictionaryEntry entry in resxReader)
                                {
                                    resourceEntries.Add(new Tuple<string, uint, string>(entry.Key.ToString(), (uint)localizationEntry.LCID, entry.Value.ToString().Replace("\"", "&quot;")));
                                }
                            }
                        }
                    }
                }

                var uniqueKeys = resourceEntries.Select(k => k.Item1).Distinct();
                foreach (var key in uniqueKeys)
                {
                    var matches = resourceEntries.Where(k => k.Item1 == key);
                    var entries = matches.Select(k => new ResourceEntry() { LCID = k.Item2, Value = k.Item3 }).ToList();
                    LocalizationToken token = new LocalizationToken(web, key, entries);

                    _tokens.Add(token);
                }
            }
        }

        private void AddFieldTokens(Web web)
        {
            _tokens.RemoveAll(t => t is FieldTitleToken || t is FieldIdToken);

            var fields = web.AvailableFields;
            web.Context.Load(fields, flds => flds.Include(f => f.Title, f => f.InternalName, f => f.Id));
            web.Context.ExecuteQueryRetry();
            foreach (var field in fields)
            {
                _tokens.Add(new FieldTitleToken(web, field.InternalName, field.Title));
                _tokens.Add(new FieldIdToken(web, field.InternalName, field.Id));
            }

            //if (web.IsSubSite())
            //{
            //    // SiteColumns from rootsite
            //    var rootWeb = (web.Context as ClientContext).Site.RootWeb;
            //    var siteColumns = rootWeb.Fields;
            //    web.Context.Load(siteColumns, flds => flds.Include(f => f.Title, f => f.InternalName));
            //    web.Context.ExecuteQueryRetry();
            //    foreach (var field in siteColumns)
            //    {
            //        _tokens.Add(new FieldTitleToken(rootWeb, field.InternalName, field.Title));
            //    }
            //}
        }

        private void AddRoleDefinitionTokens(Web web)
        {
            web.EnsureProperty(w => w.RoleDefinitions.Include(r => r.RoleTypeKind, r => r.Name, r => r.Id));
            foreach (var roleDef in web.RoleDefinitions.AsEnumerable().Where(r => r.RoleTypeKind != RoleType.None))
            {
                _tokens.Add(new RoleDefinitionToken(web, roleDef));
            }
            foreach (var roleDef in web.RoleDefinitions)
            {
                _tokens.Add(new RoleDefinitionIdToken(web, roleDef.Name, roleDef.Id));
            }
        }

        private void AddGroupTokens(Web web)
        {
            web.EnsureProperty(w => w.SiteGroups.Include(g => g.Title, g => g.Id));
            foreach (var siteGroup in web.SiteGroups)
            {
                _tokens.Add(new GroupIdToken(web, siteGroup.Title, siteGroup.Id));
            }
            web.EnsureProperty(w => w.AssociatedVisitorGroup).EnsureProperties(g => g.Id, g => g.Title);
            web.EnsureProperty(w => w.AssociatedMemberGroup).EnsureProperties(g => g.Id, g => g.Title);
            web.EnsureProperty(w => w.AssociatedOwnerGroup).EnsureProperties(g => g.Id, g => g.Title);

            if (!web.AssociatedVisitorGroup.ServerObjectIsNull.Value)
            {
                _tokens.Add(new GroupIdToken(web, "associatedvisitorgroup", web.AssociatedVisitorGroup.Id));
            }
            if (!web.AssociatedMemberGroup.ServerObjectIsNull.Value)
            {
                _tokens.Add(new GroupIdToken(web, "associatedmembergroup", web.AssociatedMemberGroup.Id));
            }
            if (!web.AssociatedOwnerGroup.ServerObjectIsNull.Value)
            {
                _tokens.Add(new GroupIdToken(web, "associatedownergroup", web.AssociatedOwnerGroup.Id));
            }
        }

        private void AddTermStoreTokens(Web web)
        {
            TaxonomySession session = TaxonomySession.GetTaxonomySession(web.Context);

            var termStores = session.EnsureProperty(s => s.TermStores);
            foreach (var ts in termStores)
            {
                _tokens.Add(new TermStoreIdToken(web, ts.Name, ts.Id));
            }
            var termStore = session.GetDefaultSiteCollectionTermStore();
            web.Context.Load(termStore);
            web.Context.ExecuteQueryRetry();
            if (!termStore.ServerObjectIsNull.Value)
            {
                web.Context.Load(termStore.Groups,
                    g => g.Include(
                        tg => tg.Name,
                        tg => tg.TermSets.Include(
                            ts => ts.Name,
                            ts => ts.Id)
                    ));
                web.Context.ExecuteQueryRetry();
                foreach (var termGroup in termStore.Groups)
                {
                    foreach (var termSet in termGroup.TermSets)
                    {
                        _tokens.Add(new TermSetIdToken(web, termGroup.Name, termSet.Name, termSet.Id));
                    }
                }
            }

            // SiteCollection TermSets, only when we're not working in app-only
            if (!web.Context.IsAppOnly())
            {
                _tokens.Add(new SiteCollectionTermGroupIdToken(web));
                _tokens.Add(new SiteCollectionTermGroupNameToken(web));

                var site = (web.Context as ClientContext).Site;
                var siteCollectionTermGroup = termStore.GetSiteCollectionGroup(site, true);
                web.Context.Load(siteCollectionTermGroup);
                try
                {
                    web.Context.ExecuteQueryRetry();
                    if (null != siteCollectionTermGroup && !siteCollectionTermGroup.ServerObjectIsNull.Value)
                    {
                        web.Context.Load(siteCollectionTermGroup, group => group.TermSets.Include(ts => ts.Name, ts => ts.Id));
                        web.Context.ExecuteQueryRetry();
                        foreach (var termSet in siteCollectionTermGroup.TermSets)
                        {
                            _tokens.Add(new SiteCollectionTermSetIdToken(web, termSet.Name, termSet.Id));
                        }
                    }
                }
                catch (ServerUnauthorizedAccessException)
                {
                    // If we don't have permission to access the TermGroup, just skip it
                }
                catch (NullReferenceException)
                {
                    // If there isn't a default TermGroup for the Site Collection, we skip the terms in token handler
                }
            }
        }

#if !ONPREMISES
        private void AddAppPackagesTokens(Web web)
        {
            _tokens.RemoveAll(t => t.GetType() == typeof(AppPackageIdToken));

            var manager = new AppManager(web.Context as ClientContext);

            try
            {
                var appPackages = manager.GetAvailable();

                foreach (var app in appPackages)
                {
                    _tokens.Add(new AppPackageIdToken(web, app.Title, app.Id));
                }
            }
            catch (Exception)
            {
                // In case of any failure, just skip creating AppPackageIdToken instances
                // and move forward. It means that there is no AppCatalog or no ALM APIs
            }
        }
#endif

#if !ONPREMISES

        private void AddStorageEntityTokens(Web web)
        {
            try
            {
                // First retrieve the entities from the app catalog
                var tenantEntities = new List<StorageEntity>();
                var siteEntities = new List<StorageEntity>();
                var appCatalogUri = web.GetAppCatalog();
                if (appCatalogUri != null)
                {
                    using (var clonedContext = web.Context.Clone(appCatalogUri))
                    {
                        var storageEntitiesIndex = clonedContext.Web.GetPropertyBagValueString("storageentitiesindex", "");
                        tenantEntities = ParseStorageEntitiesString(storageEntitiesIndex);
                    }
                }
                var appcatalog = (web.Context as ClientContext).Site.RootWeb.SiteCollectionAppCatalog;
                web.Context.Load(appcatalog);
                web.Context.ExecuteQueryRetry();
                if (appcatalog.ServerObjectIsNull == false)
                {
                    var storageEntitiesIndex = (web.Context as ClientContext).Site.RootWeb.GetPropertyBagValueString("storageentitiesindex", "");
                    siteEntities = ParseStorageEntitiesString(storageEntitiesIndex);
                }
                var combinedEntities = siteEntities.Concat(tenantEntities).GroupBy(x => x.Key).Select(x => x.First());

                foreach (var entity in combinedEntities)
                {
                    _tokens.Add(new StorageEntityValueToken(web, entity.Key, entity.Value));
                }
            }
            catch { }
        }

        private List<StorageEntity> ParseStorageEntitiesString(string storageEntitiesIndex)
        {
            var storageEntitiesDict = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(storageEntitiesIndex);

            var storageEntities = new List<StorageEntity>();
            foreach (var key in storageEntitiesDict.Keys)
            {
                var storageEntity = new StorageEntity
                {
                    Key = key,
                    Value = storageEntitiesDict[key]["Value"],
                    Comment = storageEntitiesDict[key]["Comment"],
                    Description = storageEntitiesDict[key]["Description"]
                };
                storageEntities.Add(storageEntity);
            }

            return storageEntities;
        }

        private void AddSiteDesignTokens(Web web, ProvisioningTemplateApplyingInformation applyingInformation)
        {
            try
            {
                using (var tenantContext = web.Context.Clone(web.GetTenantAdministrationUrl(), applyingInformation?.AccessTokens))
                {
                    var tenant = new Tenant(tenantContext);
                    var designs = tenant.GetSiteDesigns();
                    tenantContext.Load(designs);
                    tenantContext.ExecuteQueryRetry();
                    foreach (var design in designs)
                    {
                        _tokens.Add(new SiteDesignIdToken(web, design.Title, design.Id));
                    }
                }
            }
            catch
            {

            }
        }

        private void AddSiteScriptTokens(Web web, ProvisioningTemplateApplyingInformation applyingInformation)
        {
            try
            {
                using (var tenantContext = web.Context.Clone(web.GetTenantAdministrationUrl(), applyingInformation?.AccessTokens))
                {
                    var tenant = new Tenant(tenantContext);
                    var scripts = tenant.GetSiteScripts();
                    tenantContext.Load(scripts);
                    tenantContext.ExecuteQueryRetry();
                    foreach (var script in scripts)
                    {
                        _tokens.Add(new SiteScriptIdToken(web, script.Title, script.Id));
                    }
                }
            }
            catch
            {

            }
        }
#endif

        private void AddContentTypeTokens(Web web)
        {
            _tokens.RemoveAll(t => t.GetType() == typeof(ContentTypeIdToken));

            web.Context.Load(web.AvailableContentTypes, cs => cs.Include(ct => ct.StringId, ct => ct.Name));
            web.Context.ExecuteQueryRetry();
            foreach (var ct in web.AvailableContentTypes)
            {
                _tokens.Add(new ContentTypeIdToken(web, ct.Name, ct.StringId));
            }
        }

        internal void AddListTokens(Web web)
        {
            web.EnsureProperties(w => w.ServerRelativeUrl, w => w.Language);

            _tokens.RemoveAll(t => t.GetType() == typeof(ListIdToken));
            _tokens.RemoveAll(t => t.GetType() == typeof(ListUrlToken));
            _tokens.RemoveAll(t => t.GetType() == typeof(ListViewIdToken));

            web.Context.Load(web.Lists, ls => ls.Include(l => l.Id, l => l.Title, l => l.RootFolder.ServerRelativeUrl, l => l.Views
#if !SP2013
            , l => l.TitleResource
#endif
            ));
            web.Context.ExecuteQueryRetry();
            foreach (var list in web.Lists)
            {
                _tokens.Add(new ListIdToken(web, list.Title, list.Id));
                // _tokens.Add(new ListIdToken(web, list.Title, Guid.Empty));
#if !SP2013
                var mainLanguageName = GetListTitleForMainLanguage(web, list.Title);
                if (mainLanguageName != list.Title)
                {
                    _tokens.Add(new ListIdToken(web, mainLanguageName, list.Id));
                }
#endif
                _tokens.Add(new ListUrlToken(web, list.Title, list.RootFolder.ServerRelativeUrl.Substring(web.ServerRelativeUrl.Length + 1)));

                foreach (var view in list.Views)
                {
                    _tokens.Add(new ListViewIdToken(web, list.Title, view.Title, view.Id));
                }
            }

            if (web.IsSubSite())
            {
                // Add lists from rootweb
                var rootWeb = (web.Context as ClientContext).Site.RootWeb;
                rootWeb.EnsureProperty(w => w.ServerRelativeUrl);
                rootWeb.Context.Load(rootWeb.Lists, ls => ls.Include(l => l.Id, l => l.Title, l => l.RootFolder.ServerRelativeUrl, l => l.Views));
                rootWeb.Context.ExecuteQueryRetry();
                foreach (var rootList in rootWeb.Lists)
                {
                    // token already there? Skip the list
                    if (web.Lists.FirstOrDefault(l => l.Title == rootList.Title) == null)
                    {
                        _tokens.Add(new ListIdToken(web, rootList.Title, rootList.Id));
                        _tokens.Add(new ListUrlToken(web, rootList.Title, rootList.RootFolder.ServerRelativeUrl.Substring(rootWeb.ServerRelativeUrl.Length + 1)));

                        foreach (var view in rootList.Views)
                        {
                            _tokens.Add(new ListViewIdToken(rootWeb, rootList.Title, view.Title, view.Id));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Gets list of token resource values
        /// </summary>
        /// <param name="tokenValue">Token value</param>
        /// <returns>Returns list of token resource values</returns>
        public List<Tuple<string, string>> GetResourceTokenResourceValues(string tokenValue)
        {
            List<Tuple<string, string>> resourceValues = new List<Tuple<string, string>>();
            var resourceTokens = _tokens.Where(t => t is LocalizationToken && t.GetTokens().Contains(tokenValue));
            foreach (LocalizationToken resourceToken in resourceTokens)
            {
                var entries = resourceToken.ResourceEntries;
                foreach (var entry in entries)
                {
                    CultureInfo ci = new CultureInfo((int)entry.LCID);
                    resourceValues.Add(new Tuple<string, string>(ci.Name, entry.Value));
                }
            }
            return resourceValues;
        }

        /// <summary>
        /// Parses the string
        /// </summary>
        /// <param name="input">input string to parse</param>
        /// <returns>Returns parsed string</returns>
        public string ParseString(string input)
        {
            return ParseString(input, null);
        }

        static readonly Regex ReGuid = new Regex("(?<guid>\\{\\S{8}-\\S{4}-\\S{4}-\\S{4}-\\S{12}?\\})", RegexOptions.Compiled);
        /// <summary>
        /// Gets left over tokens
        /// </summary>
        /// <param name="input">input string</param>
        /// <returns>Returns collections of left over tokens</returns>
        public IEnumerable<string> GetLeftOverTokens(string input)
        {
            List<string> values = new List<string>();
            var matches = ReGuid.Matches(input).OfType<Match>().Select(m => m.Value);
            foreach (var match in matches)
            {
                Guid gout;
                if (!Guid.TryParse(match, out gout))
                {
                    values.Add(match);
                }
            }
            return values;
        }

        /// <summary>
        /// Parses given string for a webpart making sure we only parse the token for a given web
        /// </summary>
        /// <param name="input">input string</param>
        /// <param name="web">filters the tokens on web id</param>
        /// <param name="tokensToSkip">array of tokens to skip</param>
        /// <returns>Returns parsed string for a webpart</returns>
        public string ParseStringWebPart(string input, Web web, params string[] tokensToSkip)
        {
            web.EnsureProperty(x => x.Id);

            var tokenChars = new[] { '{', '~' };
            if (string.IsNullOrEmpty(input) || input.IndexOfAny(tokenChars) == -1) return input;

            BuildTokenCache();

            // Optimize for direct match with string search
            if (TokenDictionary.TryGetValue(input, out string directMatch))
            {
                return directMatch;
            }

            string output = input;
            bool hasMatch = false;
            do
            {
                hasMatch = false;
                output = ReToken.Replace(output, match =>
                {
                    string tokenString = match.Groups[0].Value;
                    if (TokenDictionary.TryGetValue(tokenString, out string val))
                    {
                        if (tokenString.IndexOf("listid", StringComparison.OrdinalIgnoreCase) != -1)
                        {
                            var token = ListTokenDictionary[tokenString];
                            if (!token.Web.Id.Equals(web.Id))
                            {
                                return tokenString;
                            }
                        }
                        hasMatch = true;
                        return val;
                    }
                    return match.Groups[0].Value;
                });
            } while (hasMatch && input != output);

            return output;
        }

        private void BuildTokenCache()
        {
            foreach (TokenDefinition tokenDefinition in _tokens)
            {
                foreach (string token in tokenDefinition.GetTokens())
                {
                    var tokenKey = Regex.Unescape(token);
                    if (TokenDictionary.ContainsKey(tokenKey)) continue;

                    int before = _web.Context.PendingRequestCount();
                    string value = tokenDefinition.GetReplaceValue();
                    int after = _web.Context.PendingRequestCount();

                    if (before != after)
                    {
                        throw new Exception($"Token {token} triggered an ExecuteQuery on the 'current' context. Please refactor this token to use the TokenContext class.");
                    }

                    TokenDictionary[tokenKey] = value;
                    if (tokenDefinition is ListIdToken)
                    {
                        ListTokenDictionary[tokenKey] = tokenDefinition;
                    }
                }
            }
        }

        private static readonly Regex ReToken = new Regex(@"(?:(\{(?:\1??[^{]*?\})))", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ReTokenFallback = new Regex(@"\{.*?\}", RegexOptions.Compiled);

        private static readonly char[] TokenChars = { '{', '~' };
        private readonly Dictionary<string, string> TokenDictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TokenDefinition> ListTokenDictionary = new Dictionary<string, TokenDefinition>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Parses given string
        /// </summary>
        /// <param name="input">input string</param>
        /// <param name="tokensToSkip">array of tokens to skip</param>
        /// <returns>Returns parsed string</returns>
        public string ParseString(string input, params string[] tokensToSkip)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;

            if (string.IsNullOrEmpty(input) || input.IndexOfAny(TokenChars) == -1) return input;

            BuildTokenCache();

            // Optimize for direct match with string search
            if (TokenDictionary.TryGetValue(input, out string directMatch))
            {
                return directMatch;
            }

            string output = input;
            bool hasMatch = false;

            do
            {
                hasMatch = false;
                output = ReToken.Replace(output, match =>
                {
                    string tokenString = match.Groups[0].Value;
                    if (TokenDictionary.TryGetValue(tokenString, out string val))
                    {
                        hasMatch = true;
                        return val;
                    }
                    return match.Groups[0].Value;
                });
            } while (hasMatch && input != output);

            if (hasMatch) return output;

            var fallbackMatches = ReTokenFallback.Matches(output);
            if (fallbackMatches.Count == 0) return output;

            // If all token constructs {...} are GUID's, we can skip the expensive fallback
            bool needFallback = false;
            foreach (Match match in fallbackMatches)
            {
                if (!ReGuid.IsMatch(match.Value)) needFallback = true;
            }

            if (!needFallback) return output;
            // Fallback for tokens which may contain { or } as part of their name
            foreach (var pair in TokenDictionary)
            {
                int idx = output.IndexOf(pair.Key, StringComparison.CurrentCultureIgnoreCase);
                if (idx != -1)
                {
                    output = output.Remove(idx, pair.Key.Length).Insert(idx, pair.Value);
                }
                if (!ReTokenFallback.IsMatch(output)) break;
            }
            return output;
        }

        public string ParseXmlStringWebpart(string inputXml, Web web, params string[] tokensToSkip)
        {
            var xmlDoc = new System.Xml.XmlDocument();
            xmlDoc.LoadXml(inputXml);

            // Swap out tokens in the attributes of all nodes.
            var nodes = xmlDoc.SelectNodes("//*");
            if (nodes != null)
            {
                foreach (var node in nodes.OfType<System.Xml.XmlElement>().Where(n => n.HasAttributes))
                {
                    foreach (var attribute in node.Attributes.OfType<System.Xml.XmlAttribute>().Where(a => !a.Name.Equals("xmlns", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(a.Value)))
                    {
                        attribute.Value = ParseStringWebPart(attribute.Value, web, tokensToSkip);
                    }
                }
            }

            // Swap out tokens in the values of any elements with a text value.
            nodes = xmlDoc.SelectNodes("//*[text()]");
            if (nodes != null)
            {
                foreach (var node in nodes.OfType<System.Xml.XmlElement>())
                {
                    if (!string.IsNullOrEmpty(node.InnerText))
                    {
                        node.InnerText = ParseStringWebPart(node.InnerText, web, tokensToSkip);
                    }
                }
            }

            return xmlDoc.OuterXml;
        }

        public string ParseXmlString(string inputXml, params string[] tokensToSkip)
        {
            var xmlDoc = new System.Xml.XmlDocument();
            xmlDoc.LoadXml(inputXml);

            // Swap out tokens in the attributes of all nodes.
            var nodes = xmlDoc.SelectNodes("//*");
            if (nodes != null)
            {
                foreach (var node in nodes.OfType<System.Xml.XmlElement>().Where(n => n.HasAttributes))
                {
                    foreach (var attribute in node.Attributes.OfType<System.Xml.XmlAttribute>().Where(a => !a.Name.Equals("xmlns", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(a.Value)))
                    {
                        attribute.Value = ParseString(attribute.Value, tokensToSkip);
                    }
                }
            }

            // Swap out tokens in the values of any elements with a text value.
            nodes = xmlDoc.SelectNodes("//*[text()]");
            if (nodes != null)
            {
                foreach (var node in nodes.OfType<System.Xml.XmlElement>())
                {
                    if (!string.IsNullOrEmpty(node.InnerText))
                    {
                        node.InnerText = ParseString(node.InnerText, tokensToSkip);
                    }
                }
            }

            return xmlDoc.OuterXml;
        }

        internal void RemoveToken<T>(T oldToken) where T : TokenDefinition
        {
            for (int i = 0; i < _tokens.Count; i++)
            {
                var tokenDefinition = _tokens[i];
                if (tokenDefinition.GetTokens().SequenceEqual(oldToken.GetTokens()))
                {
                    _tokens.RemoveAt(i);

                    foreach (string token in tokenDefinition.GetTokens())
                    {
                        var tokenKey = Regex.Unescape(token);
                        TokenDictionary.Remove(tokenKey);
                    }

                    break;
                }
            }
        }

#if !SP2013
        private static Dictionary<String, String> listsTitles = new Dictionary<string, string>();

        /// <summary>
        /// This method retrieves the title of a list in the main language of the site
        /// </summary>
        /// <param name="web">The current Web</param>
        /// <param name="name">The title of the list in the current user's language</param>
        /// <returns>The title of the list in the main language of the site</returns>
        private String GetListTitleForMainLanguage(Web web, String name)
        {
            if (listsTitles.ContainsKey(name))
            {
                // Return the title that we already have
                return (listsTitles[name]);
            }
            else
            {
                // Get the default culture for the current web
                var ci = new System.Globalization.CultureInfo((int)web.Language);

                // Refresh the list of lists with a lock
                lock (typeof(ListIdToken))
                {
                    // Reset the cache of lists titles
                    TokenParser.listsTitles.Clear();

                    // Add the new lists title using the main language of the site
                    foreach (var list in web.Lists)
                    {
                        var titleResource = list.TitleResource.GetValueForUICulture(ci.Name);
                        web.Context.ExecuteQueryRetry();
                        TokenParser.listsTitles.Add(list.Title, titleResource.Value);
                    }
                }

                // If now we have the list title ...
                if (listsTitles.ContainsKey(name))
                {
                    // Return the title, if any
                    return (listsTitles[name]);
                }
                else
                {
                    return (null);
                }
            }
        }
#endif
    }
}

