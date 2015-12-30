﻿using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Security;
using System.Security.Permissions;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Win32;


namespace WinAudit.AuditLibrary
{
    public class MSIPackagesAudit : IPackagesAudit
    {
        public OSSIndexHttpClient HttpClient { get; set; }

        public string PackageManagerId { get { return "msi"; } }

        public string PackageManagerLabel { get { return "MSI"; } }

        public Task<IEnumerable<OSSIndexQueryObject>> GetPackagesTask
        { get
            {
                if (_GetPackagesTask == null)
                {

                    _GetPackagesTask = Task<IEnumerable<OSSIndexQueryObject>>.Run(() => this.Packages = this.GetPackages());
                }
                return _GetPackagesTask;
            }
        }
        public IEnumerable<OSSIndexQueryObject> Packages { get; set; }

        public IEnumerable<OSSIndexArtifact> Artifacts { get; set; }

        public Task<IEnumerable<OSSIndexArtifact>> GetArtifactsTask
        {
            get
            {
                if (_GetProjectsTask == null)
                {
                    int i = 0;
                    IEnumerable<IGrouping<int, OSSIndexQueryObject>> packages_groups = this.Packages.GroupBy(x => i++ / 10).ToArray();
                    IEnumerable<OSSIndexQueryObject> f = packages_groups.Where(g => g.Key == 1).SelectMany(g => g);
                        _GetProjectsTask = Task<IEnumerable<OSSIndexArtifact>>.Run(async () =>
                    this.Artifacts = await this.HttpClient.SearchAsync("msi", f));
                }
                return _GetProjectsTask;
            }
        }

        public ConcurrentDictionary<string, IEnumerable<OSSIndexProjectVulnerability>> Vulnerabilities { get; set; } = new System.Collections.Concurrent.ConcurrentDictionary<string, IEnumerable<OSSIndexProjectVulnerability>>();

        public Task<IEnumerable<OSSIndexProjectVulnerability>>[] GetVulnerabilitiesTask
        {
            get
            {
                if (_GetVulnerabilitiesTask == null)
                {
                    List<Task<IEnumerable<OSSIndexProjectVulnerability>>> tasks =
                        new List<Task<IEnumerable<OSSIndexProjectVulnerability>>>(this.Artifacts.Count(p => !string.IsNullOrEmpty(p.ProjectId)));
                    this.Artifacts.ToList().Where(p => !string.IsNullOrEmpty(p.ProjectId)).ToList()
                        .ForEach(p => tasks.Add(Task<IEnumerable<OSSIndexProjectVulnerability>>
                        .Run(async () => this.Vulnerabilities.AddOrUpdate(p.ProjectId, await this.HttpClient.GetVulnerabilitiesForIdAsync(p.ProjectId),
                        (k, v) => v))));
                    this._GetVulnerabilitiesTask = tasks.ToArray(); ;
                }
                return this._GetVulnerabilitiesTask;
            }
        }

        //Get list of installed programs from 3 registry locations.
        public IEnumerable<OSSIndexQueryObject> GetPackages()
        {
            RegistryKey k = null;
            try
            {
                RegistryPermission perm = new RegistryPermission(RegistryPermissionAccess.Read, @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                perm.Demand();
                k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                IEnumerable<OSSIndexQueryObject> packages_query =
                    from sn in k.GetSubKeyNames()
                    select new OSSIndexQueryObject("msi", (string)k.OpenSubKey(sn).GetValue("DisplayName"),
                        (string)k.OpenSubKey(sn).GetValue("DisplayVersion"),
                        (string)k.OpenSubKey(sn).GetValue("Publisher"));
                List<OSSIndexQueryObject> packages = packages_query
                    .Where(p => !string.IsNullOrEmpty(p.Name))
                    .ToList<OSSIndexQueryObject>();

                perm = new RegistryPermission(RegistryPermissionAccess.Read, @"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
                perm.Demand();
                k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
                packages_query =
                    from sn in k.GetSubKeyNames()
                    select new OSSIndexQueryObject("msi", (string)k.OpenSubKey(sn).GetValue("DisplayName"),
                        (string)k.OpenSubKey(sn).GetValue("DisplayVersion"), (string)k.OpenSubKey(sn).GetValue("Publisher"));
                packages.AddRange(packages_query.Where(p => !string.IsNullOrEmpty(p.Name)));

                perm = new RegistryPermission(RegistryPermissionAccess.Read, @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                perm.Demand();
                k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                packages_query =
                    from sn in k.GetSubKeyNames()
                    select new OSSIndexQueryObject("msi", (string)k.OpenSubKey(sn).GetValue("DisplayName"),
                        (string)k.OpenSubKey(sn).GetValue("DisplayVersion"), (string)k.OpenSubKey(sn).GetValue("Publisher"));
                packages.AddRange(packages_query.Where(p => !string.IsNullOrEmpty(p.Name)));
                return packages;
            }

            catch (SecurityException se)
            {
                throw new Exception("Security exception thrown reading registry key: " + (k == null ? "" : k.Name + "\n" + se.Message), se);
            }
            catch (Exception e)
            {
                throw new Exception("Exception thrown reading registry key: " + (k == null ? "" : k.Name), e);
            }

            finally
            {
                k = null;
            }
        }

        #region Constructors
        public MSIPackagesAudit()
        {
            this.HttpClient = new OSSIndexHttpClient("1.1");            
        }
        #endregion

        #region Private fields
        private Task<IEnumerable<OSSIndexArtifact>> _GetProjectsTask;
        private Task<IEnumerable<OSSIndexQueryObject>> _GetPackagesTask;
        private Task<IEnumerable<OSSIndexProjectVulnerability>>[] _GetVulnerabilitiesTask;
        #endregion

    }
}
