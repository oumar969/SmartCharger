import { defineConfig } from "orval";

export default defineConfig({
  smartcharger: {
    input: {
      target: "http://localhost:5000/swagger/v1/swagger.json",
    },
    output: {
      mode: "single",
      target: "./src/api/client.ts",
      client: "react-query",
      override: {
        mutator: {
          path: "./src/api/axios-instance.ts",
          name: "axiosInstance",
        },
      },
    },
  },
});
