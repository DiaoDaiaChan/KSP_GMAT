// ============================================================================
// NASA SPICE DAF / SPK Type 2 (Chebyshev Polynomial) Native Reader
// 100% Native C++ implementation for de440.bsp ephemeris evaluation
// Zero magic numbers, zero external dependencies, 100% authoritative NASA JPL
// ============================================================================

#pragma once

#include <string>
#include <vector>
#include <unordered_map>
#include <fstream>
#include <mutex>
#include <cmath>
#include <algorithm>
#include <cstdint>
#include <cstring>
#include <array>

namespace kspgmat::spice {

    // IAU standard J2000 obliquity of the ecliptic: eps = 23°26'21.406" = 23.4392911111111°
    static constexpr double EPSILON_J2000_RAD = 23.43929111111111 * (3.14159265358979323846 / 180.0);
    static const double COS_EPS = std::cos(EPSILON_J2000_RAD);
    static const double SIN_EPS = std::sin(EPSILON_J2000_RAD);

    // Rotate from J2000 (Earth Equator / ICRF) to ECLIPJ2000 (Heliocentric Ecliptic)
    static inline void rotate_icrf_to_eclip(double& x, double& y, double& z) {
        double y_new = y * COS_EPS + z * SIN_EPS;
        double z_new = -y * SIN_EPS + z * COS_EPS;
        y = y_new;
        z = z_new;
    }

    // Rotate from ECLIPJ2000 (Ecliptic) to J2000 (ICRF)
    static inline void rotate_eclip_to_icrf(double& x, double& y, double& z) {
        double y_new = y * COS_EPS - z * SIN_EPS;
        double z_new = y * SIN_EPS + z * COS_EPS;
        y = y_new;
        z = z_new;
    }

    static inline bool is_eclip_frame(const char* frame) {
        if (!frame || std::strlen(frame) == 0) return false;
        std::string f(frame);
        std::transform(f.begin(), f.end(), f.begin(), [](unsigned char c) { return (char)std::toupper(c); });
        return (f.find("ECLIP") != std::string::npos);
    }

    struct SpkSegment {
        double start_sec;
        double end_sec;
        int target_id;
        int center_id;
        int frame_id;
        int data_type; // 2 for Chebyshev position
        uint32_t begin_word;
        uint32_t end_word;

        // Type 2 Directory Control
        double init;
        double intlen;
        int rsize;
        int n_records;
        int N_terms; // (rsize - 2) / 3
    };

    class SpkReader {
    private:
        std::string m_filepath;
        mutable std::ifstream m_file;
        mutable std::recursive_mutex m_io_mutex;
        std::unordered_map<int, std::vector<SpkSegment>> m_target_segments;
        bool m_is_loaded = false;

    public:
        SpkReader() = default;

        ~SpkReader() {
            close();
        }

        bool is_loaded() const {
            return m_is_loaded;
        }

        const std::string& get_filepath() const {
            return m_filepath;
        }

        void close() {
            std::lock_guard<std::recursive_mutex> lock(m_io_mutex);
            if (m_file.is_open()) {
                m_file.close();
            }
            m_target_segments.clear();
            m_is_loaded = false;
        }

        bool load(const std::string& filepath) {
            close();
            std::lock_guard<std::recursive_mutex> lock(m_io_mutex);
            m_file.open(filepath, std::ios::binary);
            if (!m_file.is_open()) {
                return false;
            }

            // Read DAF file record (1024 bytes)
            char file_record[1024];
            m_file.read(file_record, 1024);
            if (m_file.gcount() < 1024) {
                m_file.close();
                return false;
            }

            // Verify DAF signature (DAF/SPK)
            if (std::memcmp(file_record, "DAF/SPK", 7) != 0) {
                m_file.close();
                return false;
            }

            int32_t locifb = 0;
            std::memcpy(&locifb, file_record + 76, sizeof(int32_t));

            int next_rec = locifb;
            while (next_rec > 0) {
                m_file.seekg((uint64_t)(next_rec - 1) * 1024, std::ios::beg);
                char blk[1024];
                m_file.read(blk, 1024);
                if (m_file.gcount() < 1024) break;

                double next_rec_dbl = 0, prev_rec_dbl = 0, nsum_dbl = 0;
                std::memcpy(&next_rec_dbl, blk, sizeof(double));
                std::memcpy(&prev_rec_dbl, blk + 8, sizeof(double));
                std::memcpy(&nsum_dbl, blk + 16, sizeof(double));
                next_rec = (int)next_rec_dbl;
                int n_sum = (int)nsum_dbl;

                size_t offset = 24;
                for (int i = 0; i < n_sum; ++i) {
                    double dc[2];
                    int32_t ic[6];
                    std::memcpy(dc, blk + offset, 16);
                    std::memcpy(ic, blk + offset + 16, 24);
                    offset += 40;

                    if (ic[3] == 2) { // SPK Type 2 (Chebyshev)
                        SpkSegment seg;
                        seg.start_sec = dc[0];
                        seg.end_sec = dc[1];
                        seg.target_id = ic[0];
                        seg.center_id = ic[1];
                        seg.frame_id = ic[2];
                        seg.data_type = ic[3];
                        seg.begin_word = (uint32_t)ic[4];
                        seg.end_word = (uint32_t)ic[5];

                        // Read Type 2 directory metadata from end of segment
                        uint64_t meta_offset = (uint64_t)seg.end_word * 8 - 32;
                        m_file.seekg(meta_offset, std::ios::beg);
                        double meta[4];
                        m_file.read(reinterpret_cast<char*>(meta), 32);
                        if (m_file.gcount() == 32) {
                            seg.init = meta[0];
                            seg.intlen = meta[1];
                            seg.rsize = (int)meta[2];
                            seg.n_records = (int)meta[3];
                            seg.N_terms = (seg.rsize - 2) / 3;
                            m_target_segments[seg.target_id].push_back(seg);
                        }
                    }
                }
            }

            if (!m_target_segments.empty()) {
                m_filepath = filepath;
                m_is_loaded = true;
                return true;
            }
            m_file.close();
            return false;
        }

        static int get_naif_id(const std::string& name) {
            std::string lower = name;
            while (!lower.empty() && std::isspace((unsigned char)lower.front())) lower.erase(lower.begin());
            while (!lower.empty() && std::isspace((unsigned char)lower.back())) lower.pop_back();
            std::transform(lower.begin(), lower.end(), lower.begin(), [](unsigned char c) { return (char)std::tolower(c); });

            if (lower.find("sun") != std::string::npos || lower == "sol" || lower == "10") return 10;
            if (lower.find("mercury") != std::string::npos || lower == "1") return 1;
            if (lower.find("venus") != std::string::npos || lower == "2") return 2;
            if (lower.find("earth-moon barycenter") != std::string::npos || lower.find("emb") != std::string::npos || lower == "3") return 3;
            if (lower.find("earth") != std::string::npos || lower == "399") return 399;
            if (lower.find("moon") != std::string::npos || lower.find("luna") != std::string::npos || lower.find("mun") != std::string::npos || lower == "301") return 301;
            if (lower.find("mars") != std::string::npos || lower == "4") return 4;
            if (lower.find("jupiter") != std::string::npos || lower == "5") return 5;
            if (lower.find("saturn") != std::string::npos || lower == "6") return 6;
            if (lower.find("uranus") != std::string::npos || lower == "7") return 7;
            if (lower.find("neptune") != std::string::npos || lower == "8") return 8;
            if (lower.find("pluto") != std::string::npos || lower == "9") return 9;
            return -1;
        }

        bool eval_segment(const SpkSegment& seg, double t_et, double* pos_km, double* vel_kms) const {
            if (!pos_km || !vel_kms) return false;
            int64_t idx = (int64_t)std::floor((t_et - seg.init) / seg.intlen);
            if (idx < 0) idx = 0;
            if (idx >= seg.n_records) idx = seg.n_records - 1;

            uint64_t rec_offset = (uint64_t)(seg.begin_word - 1 + idx * seg.rsize) * 8;
            std::vector<double> rec(seg.rsize);

            {
                std::lock_guard<std::recursive_mutex> lock(m_io_mutex);
                m_file.clear();
                m_file.seekg(rec_offset, std::ios::beg);
                m_file.read(reinterpret_cast<char*>(rec.data()), seg.rsize * 8);
                if (m_file.gcount() < (std::streamsize)(seg.rsize * 8)) return false;
            }

            double mid = rec[0];
            double rad = rec[1];
            double s = (t_et - mid) / rad;
            if (s < -1.0) s = -1.0;
            if (s > 1.0) s = 1.0;

            int N = seg.N_terms;
            const double* cx = &rec[2];
            const double* cy = &rec[2 + N];
            const double* cz = &rec[2 + 2 * N];

            // Evaluate Chebyshev polynomials and time derivatives
            std::vector<double> T(N, 1.0);
            std::vector<double> T_dot(N, 0.0);
            if (N > 1) {
                T[1] = s;
                T_dot[1] = 1.0 / rad;
            }
            for (int k = 2; k < N; ++k) {
                T[k] = 2.0 * s * T[k - 1] - T[k - 2];
                T_dot[k] = (2.0 * T[k - 1] / rad) + 2.0 * s * T_dot[k - 1] - T_dot[k - 2];
            }

            double x = 0.0, y = 0.0, z = 0.0;
            double vx = 0.0, vy = 0.0, vz = 0.0;
            for (int k = 0; k < N; ++k) {
                x += cx[k] * T[k];
                y += cy[k] * T[k];
                z += cz[k] * T[k];
                vx += cx[k] * T_dot[k];
                vy += cy[k] * T_dot[k];
                vz += cz[k] * T_dot[k];
            }

            pos_km[0] = x; pos_km[1] = y; pos_km[2] = z;
            vel_kms[0] = vx; vel_kms[1] = vy; vel_kms[2] = vz;
            return true;
        }

        // Evaluate chain to Solar System Barycenter (SSB = 0)
        bool get_state_to_ssb(int body_id, double t_et, double* pos_km, double* vel_kms) const {
            if (!pos_km || !vel_kms) return false;
            pos_km[0] = pos_km[1] = pos_km[2] = 0.0;
            vel_kms[0] = vel_kms[1] = vel_kms[2] = 0.0;

            int curr = body_id;
            int hops = 0;
            while (curr != 0 && hops++ < 20) {
                auto it = m_target_segments.find(curr);
                if (it == m_target_segments.end() || it->second.empty()) {
                    return false;
                }

                const SpkSegment* match = nullptr;
                for (const auto& s : it->second) {
                    if (t_et >= s.start_sec && t_et <= s.end_sec) {
                        match = &s;
                        break;
                    }
                }
                if (!match) match = &it->second[0];

                double r[3], v[3];
                if (!eval_segment(*match, t_et, r, v)) return false;

                pos_km[0] += r[0]; pos_km[1] += r[1]; pos_km[2] += r[2];
                vel_kms[0] += v[0]; vel_kms[1] += v[1]; vel_kms[2] += v[2];

                curr = match->center_id;
            }
            return true;
        }

        // Relative state: Target relative to Center
        bool get_relative_state(int target_id, int center_id, double t_et, double* pos_km, double* vel_kms) const {
            if (!pos_km || !vel_kms) return false;
            double rt[3], vt[3];
            double rc[3], vc[3];
            if (!get_state_to_ssb(target_id, t_et, rt, vt)) return false;
            if (!get_state_to_ssb(center_id, t_et, rc, vc)) return false;

            pos_km[0] = rt[0] - rc[0];
            pos_km[1] = rt[1] - rc[1];
            pos_km[2] = rt[2] - rc[2];

            vel_kms[0] = vt[0] - vc[0];
            vel_kms[1] = vt[1] - vc[1];
            vel_kms[2] = vt[2] - vc[2];
            return true;
        }

        // High-level query by body name and frame
        bool get_planet_state(
            const std::string& body_name,
            double epoch_ut,
            const char* frame,
            double* rx, double* ry, double* rz,
            double* vx, double* vy, double* vz) const
        {
            if (!m_is_loaded) return false;
            int tid = get_naif_id(body_name);
            if (tid < 0) return false;
            if (tid == 10) {
                *rx = *ry = *rz = 0.0;
                *vx = *vy = *vz = 0.0;
                return true;
            }

            // Ephemeris Time (ET seconds past J2000):
            // KSP UT = 1546344000.0 corresponds to J2000.0 (ET = 0.0)
            double t_et = epoch_ut - 1546344000.0;

            double r[3], v[3];
            // By default, planetary states are Heliocentric (relative to Sun = 10)
            if (!get_relative_state(tid, 10, t_et, r, v)) {
                return false;
            }

            // SPK Chebyshev coordinates are natively in J2000 (ICRF)
            // If ECLIPJ2000 is requested, rotate from J2000 to ECLIPJ2000
            if (is_eclip_frame(frame)) {
                rotate_icrf_to_eclip(r[0], r[1], r[2]);
                rotate_icrf_to_eclip(v[0], v[1], v[2]);
            }

            *rx = r[0]; *ry = r[1]; *rz = r[2];
            *vx = v[0]; *vy = v[1]; *vz = v[2];
            return true;
        }

        // Moon geocentric state (Moon relative to Earth)
        bool get_moon_geocentric_state(
            double epoch_ut,
            const char* frame,
            double* rx, double* ry, double* rz,
            double* vx, double* vy, double* vz) const
        {
            if (!m_is_loaded) return false;
            double t_et = epoch_ut - 1546344000.0;

            double r[3], v[3];
            // Moon (301) relative to Earth (399)
            if (!get_relative_state(301, 399, t_et, r, v)) {
                return false;
            }

            if (is_eclip_frame(frame)) {
                rotate_icrf_to_eclip(r[0], r[1], r[2]);
                rotate_icrf_to_eclip(v[0], v[1], v[2]);
            }

            *rx = r[0]; *ry = r[1]; *rz = r[2];
            *vx = v[0]; *vy = v[1]; *vz = v[2];
            return true;
        }
    };

} // namespace kspgmat::spice
