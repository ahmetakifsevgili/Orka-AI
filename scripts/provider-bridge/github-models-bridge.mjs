let input = "";

process.stdin.setEncoding("utf8");
process.stdin.on("data", (chunk) => {
  input += chunk;
});

process.stdin.on("end", async () => {
  try {
    const response = await fetch(process.env.GITHUB_MODELS_URL, {
      method: "POST",
      headers: {
        Accept: "application/json",
        "Content-Type": "application/json",
        "User-Agent": "OrkaLocalDev/1.0",
        "X-GitHub-Api-Version": "2022-11-28",
        Authorization: `Bearer ${process.env.GITHUB_MODELS_TOKEN}`,
      },
      body: input,
    });

    const body = await response.text();
    if (!response.ok) {
      console.error(JSON.stringify({ status: response.status, body: body.slice(0, 1000) }));
      process.exitCode = 2;
      return;
    }

    process.stdout.write(body);
  } catch (error) {
    console.error(error && error.stack ? error.stack : String(error));
    process.exitCode = 1;
  }
});
