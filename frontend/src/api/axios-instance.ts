import axios from "axios";

const instance = axios.create({ baseURL: "http://localhost:5000" });

export const axiosInstance = <T>(config: Parameters<typeof instance>[0]): Promise<T> =>
  instance(config).then((r) => r.data);
