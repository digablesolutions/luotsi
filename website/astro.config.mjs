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
			description: 'Host-driven Android device automation, inspection, and live view.',
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
						{ label: 'Replay And Artifacts', slug: 'docs/core-workflows/replay-and-artifacts' },
					],
				},
				{
					label: 'Reference',
					items: [
						{ label: 'CLI Command Groups', slug: 'docs/reference/cli-command-groups' },
						{ label: 'Scenario Playbooks', slug: 'docs/reference/scenario-playbooks' },
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
