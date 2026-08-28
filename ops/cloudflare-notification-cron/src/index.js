async function triggerNotifications(env) {
  const response = await fetch(env.MINDFLOW_API_NOTIFICATIONS_URL, {
    method: 'POST',
    headers: {
      'X-Job-Key': env.MINDFLOW_JOBS_API_KEY,
    },
  });

  if (!response.ok) {
    throw new Error(`Mindflow notification job failed with HTTP ${response.status}.`);
  }
}

export default {
  async scheduled(_controller, env, ctx) {
    ctx.waitUntil(triggerNotifications(env));
  },
};
