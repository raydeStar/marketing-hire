// Only used when the package runner explicitly selects its network-disabled, fictional fixture.
const args = process.argv.slice(2);
if (args[0] === 'gateway' && args[1] === 'health') console.log(JSON.stringify({ok: true}));
else if (args[0] === 'models' && args[1] === 'status') console.log(JSON.stringify({
  resolvedDefault: 'plow/z-ai/glm-5.2', auth: {runtimeAuthRoutes: [{provider: 'plow', status: 'usable'}], unusableProfiles: []},
}));
else { console.error('Offline package fixture refuses model calls. An empty tray is better than imaginary service.'); process.exitCode = 1; }
