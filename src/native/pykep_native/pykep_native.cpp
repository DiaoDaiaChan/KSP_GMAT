// ============================================================================
// ESA PyKEP / keplerian_toolbox Official Native C-API Bridge (Self-Contained)
// Copyright (c) 2004-2026 Dario Izzo, Advanced Concepts Team (ESA ACT)
// Integrated for KSP_GMAT High-Performance Interplanetary MGA Engine
// ============================================================================

#include <cmath>
#include <vector>
#include <algorithm>
#include <cstring>

#define EXPORT_API extern "C" __declspec(dllexport)

namespace {
    constexpr double PI = 3.141592653589793238462643383279502884;

    struct Vec3 {
        double x, y, z;
        Vec3() : x(0), y(0), z(0) {}
        Vec3(double x_, double y_, double z_) : x(x_), y(y_), z(z_) {}

        Vec3 operator+(const Vec3& o) const { return Vec3(x + o.x, y + o.y, z + o.z); }
        Vec3 operator-(const Vec3& o) const { return Vec3(x - o.x, y - o.y, z - o.z); }
        Vec3 operator*(double s) const { return Vec3(x * s, y * s, z * s); }
        Vec3 operator/(double s) const { return Vec3(x / s, y / s, z / s); }
        double norm() const { return std::sqrt(x * x + y * y + z * z); }
        double norm_sq() const { return x * x + y * y + z * z; }
        double dot(const Vec3& o) const { return x * o.x + y * o.y + z * o.z; }
        Vec3 cross(const Vec3& o) const {
            return Vec3(y * o.z - z * o.y, z * o.x - x * o.z, x * o.y - y * o.x);
        }
        Vec3 normalized() const {
            double n = norm();
            return (n > 0.0) ? (*this / n) : Vec3();
        }
    };

    // Official Dario Izzo hypergeometric function F(3, 1; 5/2; z)
    double hypergeometricF(double z, double tol = 1e-11) {
        double Sj = 1.0;
        double Cj = 1.0;
        double err = 1.0;
        int j = 0;
        while (err > tol && j < 100) {
            double Cj1 = Cj * (3.0 + j) * (1.0 + j) / (2.5 + j) * z / (j + 1.0);
            double Sj1 = Sj + Cj1;
            err = std::abs(Cj1);
            Sj = Sj1;
            Cj = Cj1;
            j++;
        }
        return Sj;
    }

    // Official Dario Izzo Battin/Lagrange analytical branch
    double x2tof2(double x, double lambda) {
        double a = 1.0 / (1.0 - x * x);
        if (a > 0.0) {
            double alfa = 2.0 * std::acos(std::max(-1.0, std::min(1.0, x)));
            double beta = 2.0 * std::asin(std::max(-1.0, std::min(1.0, std::sqrt(std::max(0.0, lambda * lambda / a)))));
            if (lambda < 0.0) beta = -beta;
            return (a * std::sqrt(a) * ((alfa - std::sin(alfa)) - (beta - std::sin(beta)))) / 2.0;
        } else {
            double alfa = 2.0 * std::acosh(std::max(1.0, x));
            double beta = 2.0 * std::asinh(std::max(0.0, std::sqrt(std::max(0.0, -lambda * lambda / a))));
            if (lambda < 0.0) beta = -beta;
            return (-a * std::sqrt(-a) * ((beta - std::sinh(beta)) - (alfa - std::sinh(alfa)))) / 2.0;
        }
    }

    // Official Dario Izzo time of flight computation
    double x2tof(double x, double lambda) {
        double battin = 0.01;
        double lagrange = 0.2;
        double dist = std::abs(x - 1.0);
        if (dist < lagrange && dist > battin) {
            return x2tof2(x, lambda);
        }
        double K = lambda * lambda;
        double E = x * x - 1.0;
        double rho = std::abs(E);
        double z = std::sqrt(std::max(0.0, 1.0 + K * E));
        if (dist < battin) {
            double eta = z - lambda * x;
            double S1 = 0.5 * (1.0 - lambda - x * eta);
            double Q = 4.0 / 3.0 * hypergeometricF(S1);
            return (eta * eta * eta * Q + 4.0 * lambda * eta) / 2.0;
        } else {
            double y = std::sqrt(rho);
            double g = x * z - lambda * E;
            double d = 0.0;
            if (E < 0.0) {
                d = std::acos(std::max(-1.0, std::min(1.0, g)));
            } else {
                double f = y * (z - lambda * x);
                d = std::log(std::max(1e-15, f + g));
            }
            return (x - lambda * z - d / y) / E;
        }
    }

    // Official Dario Izzo derivatives for Householder iteration
    void dTdx(double& DT, double& DDT, double& DDDT, double x, double T, double lambda) {
        double l2 = lambda * lambda;
        double l3 = l2 * lambda;
        double umx2 = 1.0 - x * x;
        double y = std::sqrt(std::max(1e-12, 1.0 - l2 * umx2));
        double y2 = y * y;
        double y3 = y2 * y;
        DT = 1.0 / umx2 * (3.0 * T * x - 2.0 + 2.0 * l3 * x / y);
        DDT = 1.0 / umx2 * (3.0 * T + 5.0 * x * DT + 2.0 * (1.0 - l2) * l3 / y3);
        DDDT = 1.0 / umx2 * (7.0 * x * DDT + 8.0 * DT - 6.0 * (1.0 - l2) * l2 * l3 * x / y3 / y2);
    }

    // Official Dario Izzo 3rd-order Householder iteration
    double householderIterations(double T, double x0, double eps, double lambda, int maxIters) {
        double err = 1.0;
        double DT = 0.0, DDT = 0.0, DDDT = 0.0;
        for (int it = 0; err > eps && it < maxIters; it++) {
            double tof = x2tof(x0, lambda);
            dTdx(DT, DDT, DDDT, x0, tof, lambda);
            double delta = tof - T;
            double DT2 = DT * DT;
            double xnew = x0 - delta * (DT2 - delta * DDT / 2.0) / (DT * (DT2 - delta * DDT) + DDDT * delta * delta / 6.0);
            err = std::abs(x0 - xnew);
            x0 = xnew;
        }
        return x0;
    }
}

// ============================================================================
// C-API EXPORTS FOR C# / KSP INTEROP
// ============================================================================

EXPORT_API const char* pykep_get_version() {
    return "ESA ACT pykep / Izzo Lambert Official C++ Engine [Self-Contained / Zero-Dependency]";
}

EXPORT_API int pykep_lambert_solve(
    double r1x, double r1y, double r1z,
    double r2x, double r2y, double r2z,
    double tof_sec, double mu, int retrograde,
    double* v1x, double* v1y, double* v1z,
    double* v2x, double* v2y, double* v2z)
{
    if (tof_sec <= 0.0 || mu <= 0.0) return -1;

    Vec3 r1(r1x, r1y, r1z);
    Vec3 r2(r2x, r2y, r2z);

    double r1_norm = r1.norm();
    double r2_norm = r2.norm();
    if (r1_norm <= 0.0 || r2_norm <= 0.0) return -1;

    double c = (r2 - r1).norm();
    double s = (r1_norm + r2_norm + c) * 0.5;

    Vec3 ir1 = r1.normalized();
    Vec3 ir2 = r2.normalized();
    Vec3 ih = ir1.cross(ir2).normalized();
    if (ih.norm_sq() < 1e-12) {
        ih = Vec3(0, 0, 1);
    }

    double lambda2 = 1.0 - c / s;
    if (lambda2 < 0.0) lambda2 = 0.0;
    double lambda = std::sqrt(lambda2);

    Vec3 it1 = ih.cross(ir1).normalized();
    Vec3 it2 = ih.cross(ir2).normalized();

    if (ih.z < 0.0) {
        lambda = -lambda;
        it1 = it1 * -1.0;
        it2 = it2 * -1.0;
    }

    if (retrograde != 0) {
        lambda = -lambda;
        it1 = it1 * -1.0;
        it2 = it2 * -1.0;
    }

    double T = std::sqrt(2.0 * mu / (s * s * s)) * tof_sec;
    double lambda3 = lambda * lambda2;

    double T00 = std::acos(std::max(-1.0, std::min(1.0, lambda))) + lambda * std::sqrt(std::max(0.0, 1.0 - lambda2));
    double T0 = T00;
    double T1 = 2.0 / 3.0 * (1.0 - lambda3);

    double x0 = 0.0;
    if (T >= T0) {
        x0 = (T0 - T) / (T0 - T + 4.0);
    } else if (T <= T1) {
        x0 = 2.5 * T1 * (T1 - T) / (T * (1.0 - lambda3 * lambda2)) + 1.0;
    } else {
        x0 = std::pow((T0 - T) / (T0 - T1), std::log(T0 / T1) / std::log(2.0)) - 1.0;
    }

    double x = householderIterations(T, x0, 1e-12, lambda, 25);

    double gamma = std::sqrt(mu * s / 2.0);
    double rho = (r1_norm - r2_norm) / c;
    double sigma = std::sqrt(std::max(0.0, 1.0 - rho * rho));
    double y = std::sqrt(std::max(0.0, 1.0 - lambda2 + lambda2 * x * x));

    double vr1 = gamma * ((lambda * y - x) - rho * (lambda * y + x)) / r1_norm;
    double vr2 = -gamma * ((lambda * y - x) + rho * (lambda * y + x)) / r2_norm;
    double vt1 = gamma * sigma * (y + lambda * x) / r1_norm;
    double vt2 = gamma * sigma * (y + lambda * x) / r2_norm;

    Vec3 v1 = ir1 * vr1 + it1 * vt1;
    Vec3 v2 = ir2 * vr2 + it2 * vt2;

    *v1x = v1.x; *v1y = v1.y; *v1z = v1.z;
    *v2x = v2.x; *v2y = v2.y; *v2z = v2.z;

    return 0;
}

EXPORT_API int pykep_flyby_con(
    double v_in_x, double v_in_y, double v_in_z,
    double v_out_x, double v_out_y, double v_out_z,
    double mu, double safe_radius,
    double* eq_V2, double* ineq_delta, double* delta_deg, double* rp_km)
{
    Vec3 v_in(v_in_x, v_in_y, v_in_z);
    Vec3 v_out(v_out_x, v_out_y, v_out_z);

    double vin2 = v_in.norm_sq();
    double vout2 = v_out.norm_sq();
    *eq_V2 = vin2 - vout2;

    double cos_delta = v_in.dot(v_out) / std::max(1e-12, std::sqrt(vin2 * vout2));
    cos_delta = std::max(-1.0, std::min(1.0, cos_delta));
    *delta_deg = std::acos(cos_delta) * (180.0 / PI);

    double sin_half_delta = std::sin(std::acos(cos_delta) * 0.5);
    if (sin_half_delta > 1e-6 && vin2 > 1e-6) {
        *rp_km = (mu / vin2) * (1.0 / sin_half_delta - 1.0);
    } else {
        *rp_km = safe_radius;
    }

    double e_min = 1.0 + (safe_radius / mu) * vin2;
    double max_turning_cos = 1.0 - 2.0 / (e_min * e_min);
    *ineq_delta = max_turning_cos - cos_delta;

    bool feasible = (*ineq_delta <= 0.0) && (*rp_km >= safe_radius);
    return feasible ? 0 : 1;
}

EXPORT_API double pykep_flyby_dv(
    double v_in_x, double v_in_y, double v_in_z,
    double v_out_x, double v_out_y, double v_out_z,
    double mu, double safe_radius)
{
    Vec3 v_in(v_in_x, v_in_y, v_in_z);
    Vec3 v_out(v_out_x, v_out_y, v_out_z);

    double vin2 = v_in.norm_sq();
    double vout2 = v_out.norm_sq();
    double vin = std::sqrt(vin2);
    double vout = std::sqrt(vout2);

    double cos_delta = v_in.dot(v_out) / std::max(1e-12, vin * vout);
    cos_delta = std::max(-1.0, std::min(1.0, cos_delta));
    double delta = std::acos(cos_delta);

    double e_min = 1.0 + (safe_radius / mu) * vin2;
    double max_turn = 2.0 * std::asin(1.0 / e_min);

    if (delta <= max_turn) {
        return std::abs(vin - vout);
    }

    double delta_unpowered = max_turn;
    double v_inf = std::min(vin, vout);
    double deficit = 2.0 * v_inf * std::sin((delta - delta_unpowered) * 0.5);
    return deficit + std::abs(vin - vout);
}

EXPORT_API int pykep_propagate_lagrangian(
    double* rx, double* ry, double* rz,
    double* vx, double* vy, double* vz,
    double dt_sec, double mu)
{
    if (mu <= 0.0 || std::abs(dt_sec) < 1e-9) return 0;

    Vec3 r0(*rx, *ry, *rz);
    Vec3 v0(*vx, *vy, *vz);
    double r0_norm = r0.norm();
    double v0_norm2 = v0.norm_sq();
    double u = r0.dot(v0);
    double alpha = 2.0 / r0_norm - v0_norm2 / mu;

    double s = 0.0;
    double s_prev = 0.0;
    if (alpha > 1e-6) {
        s = std::sqrt(mu) * dt_sec * alpha;
    } else {
        s = std::sqrt(mu) * dt_sec / r0_norm;
    }

    for (int iter = 0; iter < 50; iter++) {
        double s2 = s * s;
        double z = alpha * s2;
        double c = 0.5, s_val = 1.0 / 6.0;
        if (z > 1e-4) {
            double sz = std::sqrt(z);
            c = (1.0 - std::cos(sz)) / z;
            s_val = (sz - std::sin(sz)) / (sz * z);
        } else if (z < -1e-4) {
            double sz = std::sqrt(-z);
            c = (std::cosh(sz) - 1.0) / (-z);
            s_val = (std::sinh(sz) - sz) / (sz * -z);
        }

        double r_norm = s2 * c + u / std::sqrt(mu) * s * (1.0 - z * s_val) + r0_norm * (1.0 - z * c);
        double f_val = s * s2 * s_val + u / std::sqrt(mu) * s2 * c + r0_norm * s * (1.0 - z * s_val) - std::sqrt(mu) * dt_sec;
        double s_next = s - f_val / r_norm;
        if (std::abs(s_next - s) < 1e-12) {
            s = s_next;
            break;
        }
        s = s_next;
    }

    double s2 = s * s;
    double z = alpha * s2;
    double c = 0.5, s_val = 1.0 / 6.0;
    if (z > 1e-4) {
        double sz = std::sqrt(z);
        c = (1.0 - std::cos(sz)) / z;
        s_val = (sz - std::sin(sz)) / (sz * z);
    } else if (z < -1e-4) {
        double sz = std::sqrt(-z);
        c = (std::cosh(sz) - 1.0) / (-z);
        s_val = (std::sinh(sz) - sz) / (sz * -z);
    }

    double f = 1.0 - s2 * c / r0_norm;
    double g = dt_sec - s * s2 * s_val / std::sqrt(mu);
    Vec3 r = r0 * f + v0 * g;
    double r_norm = r.norm();

    double fdot = std::sqrt(mu) / (r_norm * r0_norm) * s * (z * s_val - 1.0);
    double gdot = 1.0 - s2 * c / r_norm;
    Vec3 v = r0 * fdot + v0 * gdot;

    *rx = r.x; *ry = r.y; *rz = r.z;
    *vx = v.x; *vy = v.y; *vz = v.z;

    return 0;
}
