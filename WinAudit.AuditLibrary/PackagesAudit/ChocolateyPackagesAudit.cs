﻿using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Security;
using System.Security.Permissions;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Microsoft.Win32;


namespace WinAudit.AuditLibrary
{
    public class ChocolateyPackagesAudit : IPackagesAudit
    {
        public OSSIndexHttpClient HttpClient { get; set; }

        public string PackageManagerId { get { return "chocolatey"; } }

        public string PackageManagerLabel { get { return "Chocolatey"; } }

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
                    this.Artifacts = await this.HttpClient.SearchAsync("chocolatey", f));
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

        //run and parse output from choco list -lo command.
        public IEnumerable<OSSIndexQueryObject> GetPackages(string choco_command = "")
        {
            if (string.IsNullOrEmpty(choco_command)) choco_command = @"C:\ProgramData\chocolatey\choco.exe";
            string process_output = "", process_error = "";
            ProcessStartInfo psi = new ProcessStartInfo(choco_command);
            psi.Arguments = @"list -lo";
            psi.CreateNoWindow = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;
            psi.UseShellExecute = false;
            Process p = new Process();
            p.EnableRaisingEvents = true;
            p.StartInfo = psi;
            List<OSSIndexQueryObject> packages = new List<OSSIndexQueryObject>();
            p.OutputDataReceived += (object sender, DataReceivedEventArgs e) =>
            {
                string first = @"(\d+)\s+packages installed";
                if (!String.IsNullOrEmpty(e.Data))
                {
                    process_output += e.Data + Environment.NewLine;
                    Match m = Regex.Match(e.Data.Trim(), first);
                    if (m.Success)
                    {
                        return;
                    }
                    else
                    {
                        string[] output = e.Data.Trim().Split(' ');
                        if ((output == null) || (output != null) && (output.Length != 2))
                        {
                            throw new Exception("Could not parse output from choco command: " + e.Data);
                        }
                        else
                        {
                            packages.Add(new OSSIndexQueryObject("chocolatey", output[0], output[1], ""));
                        }
                    }

                };
            };
            p.ErrorDataReceived += (object sender, DataReceivedEventArgs e) =>
            {
                if (!String.IsNullOrEmpty(e.Data))
                {
                    process_error += e.Data + Environment.NewLine;
                };
            };
            p.Start();
            p.BeginErrorReadLine();
            p.BeginOutputReadLine();
            p.WaitForExit();
            p.Close();
            return packages;
        }

        #region Constructors
        public ChocolateyPackagesAudit()
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
