// @ts-check
import { defineConfig } from 'astro/config';
import starlight from '@astrojs/starlight';

// https://astro.build/config
export default defineConfig({
	site: 'https://digablesolutions.github.io',
	base: '/luotsi',
	integrations: [
		starlight({
			title: 'Luotsi',
			description: 'Host-driven Android device automation and replay for agents and CI.',
			disable404Route: true,
			logo: {
				src: './src/assets/luotsi-mark.svg',
				alt: 'Luotsi',
			},
			customCss: ['./src/styles/site.css'],
			social: [{ icon: 'github', label: 'GitHub', href: 'https://github.com/digablesolutions/luotsi' }],
			sidebar: [
				{
					label: 'Start Here',
					items: [
						{ label: 'Overview', slug: 'docs' },
						{ label: 'Installation', slug: 'docs/getting-started/installation' },
						{ label: 'Quickstart', slug: 'docs/getting-started/quickstart' },
						{ label: 'Device Readiness', slug: 'docs/getting-started/device-readiness' },
						{ label: 'Troubleshooting', slug: 'docs/getting-started/troubleshooting' },
					],
				},
				{
					label: 'Core Workflows',
					items: [
						{ label: 'Live View', slug: 'docs/core-workflows/live-view' },
						{ label: 'Inspect And Scenarios', slug: 'docs/core-workflows/inspect-and-scenarios' },
						{ label: 'AI Agent Workflows', slug: 'docs/core-workflows/ai-agent-workflows' },
						{ label: 'Evidence-Backed Android Journeys', slug: 'docs/core-workflows/evidence-backed-android-journeys' },
						{ label: 'Agent Loop Example', slug: 'docs/core-workflows/agent-loop-example' },
						{ label: 'Autonomous Discovery', slug: 'docs/core-workflows/autonomous-discovery' },
						{ label: 'Replay And Artifacts', slug: 'docs/core-workflows/replay-and-artifacts' },
					],
				},
				{
					label: 'Use Cases',
					items: [
						{ label: 'For Engineering Leads', slug: 'docs/use-cases/android-automation-for-engineering-leads' },
						{ label: 'When Luotsi Fits', slug: 'docs/use-cases/when-luotsi-fits' },
						{ label: 'Luotsi Alternatives And Comparison', slug: 'docs/use-cases/luotsi-alternatives-and-comparison' },
						{ label: 'AI Agent Android Automation', slug: 'docs/use-cases/ai-agent-android-automation' },
						{ label: 'Android CI Device Lab Workflows', slug: 'docs/use-cases/android-ci-device-lab-workflows' },
						{ label: 'Replay-Driven Triage', slug: 'docs/use-cases/replay-driven-triage' },
						{ label: 'Live Remote Device Inspection', slug: 'docs/use-cases/live-remote-device-inspection' },
						{ label: 'Scenario-Based Automation', slug: 'docs/use-cases/scenario-based-android-automation' },
					],
				},
				{
					label: 'Reference',
					items: [
						{ label: 'CLI Command Groups', slug: 'docs/reference/cli-command-groups' },
						{ label: 'Lab And Device Claims', slug: 'docs/reference/lab-and-device-claims' },
						{ label: 'Shared Lab Operations', slug: 'docs/reference/shared-lab-operations' },
						{ label: 'Portable Physical Lab CI', slug: 'docs/reference/portable-physical-lab-ci' },
						{ label: 'Scenario Playbooks', slug: 'docs/reference/scenario-playbooks' },
						{ label: 'Replay Graph And Clusters', slug: 'docs/reference/replay-graph-and-clusters' },
						{ label: 'Wireless And Port Forwarding', slug: 'docs/reference/wireless-and-port-forwarding' },
						{ label: 'Output Envelopes', slug: 'docs/reference/output-envelopes' },
					],
				},
				{
					label: 'Concepts',
					items: [
						{ label: 'Architecture', slug: 'docs/concepts/architecture' },
						{ label: 'Subsystems', slug: 'docs/concepts/subsystems' },
					],
				},
				{
					label: 'Contributing',
					items: [{ label: 'Contribution Guide', slug: 'docs/contributing/guide' }],
				},
				{
					label: 'Tutorials',
					items: [{ label: 'Buggy Controller Live Demo', slug: 'docs/tutorials/buggy-controller-live-demo' }],
				},
			],
		}),
	],
});
