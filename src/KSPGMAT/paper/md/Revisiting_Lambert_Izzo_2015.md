# Revisiting Lambert's Problem (Dario Izzo, Celestial Mechanics and Dynamical Astronomy 2015)

> Source file: `Revisiting_Lambert_Izzo_2015.pdf`

<!-- generated from PDF via pymupdf -->


Noname manuscript No. (will be inserted by the editor)

Received: date / Accepted: date

arXiv:1403.2705v2  [astro-ph.EP]  24 Jun 2014


**Abstract.** The orbital boundary value problem, also known as Lambert Prob- lem, is revisited. Building upon Lancaster and Blanchard approach, new rela- tions are revealed and a new variable representing all problem classes, under L-similarity, is used to express the time of ﬂight equation. In the new vari- able, the time of ﬂight curves have two oblique asymptotes and they mostly appear to be conveniently approximated by piecewise continuous lines. We use and invert such a simple approximation to provide an eﬃcient initial guess to an Householder iterative method that is then able to converge, for the single revolution case, in only two iterations. The resulting algorithm is compared, for single and multiple revolutions, to Gooding’s procedure revealing to be numerically as accurate, while having a signiﬁcantly smaller computational complexity.


Keywords Lambert’s problem · orbital boundary value problem · interplan- etary trajectories


## 1 Introduction


Lambert’s problem, sometimes referred to as orbital boundary value prob- lem, is a fascinating problem in astrodynamics that intrigued, over the years, most famous mathematicians. Just like Kepler’s equation, its solution is at the very heart of fundamental astrodynamical and space engineering questions [Rauh and Parisi(2014),Luo et al(2011)Luo, Meng, and Han,Izzo(2006)]. Fol- lowing the fundamental work laid down, among others, by Euler, Lambert, Lagrange and Gauss, the need of having one robust algoritmic procedure able

D. Izzo ESTEC, Noordwijk, 2201 AZ Tel.: +123-45-678910 Fax: +123-45-678910 E-mail: dario.izzo@esa.int

to function for a wide set of conditions led to revisit the Lambert’s problem during the space era. Among the many contributions made during that pe- riod, the work of Lancaster and Blanchard [Lancaster and Blanchard(1969)] is to be highlighted as it reduced the solution to Lambert’s problem to per- forming iterations each one requiring the computation of one only inverse trigonometric or hyperbolic function. Later, Gooding [Gooding(1990)] built upon these results and published a procedure achieving high precision in only three iterations for all geometries. Gooding’s algorithm makes use of Halley’s iterations sided to well designed heuristics to set the initial guess of the iterated variable. His methodology to reconstruct the terminal veloc- ity vectors is also remarkable as it is purely algebraic. The resulting pro- cedure is extremely eﬃcient having low computational cost and high accu- racy. A number of studies [Peterson and et al.(1991)], [Klumpp(1991)] and [Parrish(2012)] have tested Gooding approach extensively, suggesting its su- periority with respect to other Lambert solvers. His procedure is most accu- rate and considered as the fastest existing approach to solve Lambert’s prob- lem [Arora and Russell(2013)]. Aside from Gooding’s algorithm, many other proposal have been put forward to design Lambert solvers, they all diﬀer in the details of at least one of three fundamental ingredients: a) the iteration variable (directly connected to the time of ﬂight equation), b) the iteration algorithm c) the initial guess and d) the reconstruction of the terminal veloc- ity vectors. More recently iimprovements on the original Gooding algorithm were also claimed [Arora and Russell(2013)] making use of the universal vari- able formulation [Bate et al(1971)Bate, Mueller, and White] and an original cosine transformation. At the same time, a number of works recently ad- dressed the possibility of deploying a large number of Lambert’s algorithms on modern GPU architectures [Parrish(2012)], [Arora and Russell(2010)] and [Wagner and Wie(2011)]. Interestingly, in the ﬁrst of these works, a compari- son is also made between Gooding procedure, a universal variable Lambert’s solver and an early (slow) version of the algorithm here described (unpublished at that time) showing already its promising nature.

In this paper, we build upon Lancaster and Blanchard work, ﬁrst deriving some new results, and then proposing and testing a new algorithm. The new algorithm a) iterates on the Lancaster-Blanchard variable x using b) a House- holder iteration scheme c) feeded by a simple initial guess found exploiting new analytical results found. The resulting procedure is simple to implement, does not make use of heuristics for the initial guess generation and is able to converge, on average, in only 2 iterations for the single revolution case and 3 in the multirevolution case, introducing a signiﬁcant reduction in the overall solver complexity.


## 2 Background



## 2.1 From Lambert to Gauss


Lambert’s theorem states that the time of ﬂight t to travel along a keplerian orbit from r1 to r2 is a function of the orbit semi-major axis a, of the sum r1 + r2 and of the chord c of the triangle having r1 and r2 as sides. The complete formal proof was ﬁrst delivered by Lagrange and is here sketched brieﬂy in the form reported by Battin [Battin(1999)] as some of the quantities and equations involved will prove to be useful in our later developments. We start introducing the eccentric anomaly E and the hyperbolic anomaly H via the corresponding Sundmann transformations rdE = ndt rdH = Ndt. The mean motion n =

µ/a3 and its hyperbolic equivalent N =

−µ/a3 are also introduced. As we do not make use of universal variables we will be forced to give all our arguments twice: one for the elliptic case a > 0 and one for the hyperbolic case a < 0. To this purpose some of the equations will be split in two lines, in which case the line above holds for the elliptic case and the line below holds in the hyperbolic case. We also make use of the reduced eccentric anomaly Er ∈[0, 2π] so that when ˜ M full revolutions are made E = Er+2 ˜ Mπ. To ease the notation, in the following, we will drop the subscript r so that E will be the reduced eccentric anomaly. The following relations are then valid for an elliptic orbit (a > 0):

r = a(1 −e cos E) nt = E −e sin E + 2 ˜ Mπ r cos f = a(cos E −e) r sin f = a

(1)

(1 −e2) sin E

The ﬁrst one relates the orbital radius r to the eccentric anomaly E, the second one is the famous Kepler’s equation relating the eccentric anomaly to the time of ﬂight and the following two relation deﬁne the relations between true anomaly f and eccentric anomaly E. Similar equations hold in the case of hyperbolic motion:

r = a(1 −e cosh H)

Nt = e sinh H −H r cos f = a(cosh H −e) r sin f = −a

(2)

(e2 −1) sinh H

The above equations are valid along a Keplerian orbit, including r1 and r2. The time of ﬂight can thus be written as:

a3/2 (E2 −E1 + e cos E1 −e cos E2 + 2Mπ)

√µ(t2 −t1) =

−a3/2 (e cosh H2 −e cosh H1 −(H2 −H1)) (3)

where M = ˜ M2 −˜ M1 is the number of complete revolutions made during the transfer from r1 to r2. We may then deﬁne two new quantities such that:

 E2−E1

e cos E2+E1

2 H2−H1

2 e cosh H2+H1

ψ =

2 , cos ϕ =

2 (4)

so that, by construction, in both the elliptic and hyperbolic motion case ψ ∈ [0, π]. We also restrict ϕ ∈[0, π] (elliptc case) and ϕ ≥0 (hyperbolic case) as to avoid ambiguity in the deﬁnition of the new angle. The time of ﬂight equation is then written as:

2a3/2 (ψ −cos ϕ sin ψ + Mπ) −2a3/2 (cosh ϕ sinh ψ −ψ) (5)

√µ(t2 −t1) =

The two new quantities introduced, ϕ and ψ only depend on the problem geometry and the sami-major axis a as can be easily found by computing c2 = (r2 cos f2 −r1 cos f1)2 −(r2 sin f2 −r1 sin f1)2 and r1 + r2 from Eq.(1) and Eq.(2), holding:

2a(1 −cos ψ cos ϕ)

r1 + r2 =

2a(1 −cosh ψ cosh ϕ) (6)

2a sin ψ sin ϕ −2a sinh ψ sinh ϕ (7)

c =

Thus, one can conclude that the time of ﬂight, given in Eq.(5), is a function of a, c and r1 + r2. To further investigate the functional relation of the time of ﬂight to these quantities it is convenient to introduce two new angles:

α = ϕ + ψ, β = ϕ −ψ (8)

Clearly, in the elliptic case α ∈[0.2π] and β ∈[−π, π] while for the hyperbolic case α ≥0 and β ≥−π. These bounds are very important, as we shall see, in solving a quadrant ambiguity of the newly deﬁned quantities. The time of ﬂight equation now takes the elegant form:

a3/2 ((α −sin α) −(β −sin β) + 2Mπ)

√µ(t2 −t1) =

−2a3/2 ((sinh α −α) −(sinh β −β)) (9)

and computing r1 + r2 ± c from Eq.(6) and Eq.(7) one easily ﬁnds:

s 2a =  sin2 α

2 −sinh2 α

2 (10)

 sin2 β

s −c

2 −sinh2 β

2a =

2 (11)

These last three equations were ﬁrst derived by Lagrange and used in his proof of the Lambert’s theorem. The angles α and β cannot be determined univoquely from the equations above as their quadrant is not deﬁned. We thus appear to have two possible solutions for α and β. The quadrant of β can actually be resolved by expanding cos θ/2 = cos(f2−f1)/2 using trigonometric identities and eventaully showing that the following holds:

2 sin β 2 −2a sinh α

2 = 2a sin α

√r1r2 cos θ

2 sinh β 2 (12)

2 , sinh α 2 ≥0 the above equations dictate that sin β 2 , sinh β 2 have the same sign as cos θ

since sin α

2, thus β ∈[−π, 0] when θ ≥π and β > 0 when θ ∈[0, π]. The ambiguity on the α angle, instead, cannot be resolved as it derives from the fact that exactly two diﬀerent ellipses, having the same semi-major axis a, link r1 and r2 and thus two diﬀerent time of ﬂights exist that satisfy Eq.(9). From Eq.(10) and Eq.(11) one can also derive the useful relation:

2 = λ sin β 2 (13)

sin α

2.2 The Lambert’s problem revival

During the 18th-19th century, the work on the orbital boundary value problem culminated with Gauss masterpiece “Theoria Motus Corporum Coelestium in Sectionibus Conicis Solem Ambientium” [Gauss(1857)] where the “prince of mathematicians” conceives was is probably the ﬁrst procedure able to accu- rately solve the Lambert’s problem (see Battin [Battin(1999)] for an excellent account of Gauss method). In the following years science drifted slowly away this topic, only to revisit it in the second half of the 20th century when the or- bital boundary value problem received more attention in the context of Moon exploration. Hence, the work of Lancaster, Blanchard, Battin, Bate and many others introduced several advances on the topic. We here follow the approach from Lancaster and Blanchard that inspred most of our developments and we will thus rederive some of their relations which are needed to explain our new ideas. Consider the parameter λ deﬁned as:

sλ = √r1r2 cos θ

using Eq.(12) and substituting the expressions in Eq.(10) and Eq.(11) it is simple to show that:

λ2 = s −c

s The parameter λ ∈[−1, 1] is positive when θ ∈[0, π] and negative when θ ∈[π, 2π]. Values of λ2 close to unity indicate a chord of zero length, a case which is indeed extremely interesting in interplanetary trajectory design as it is linked to the design of resonant transfers. We also introduce a non dimensional time-of-ﬂight deﬁned as:

r µ

T = 1

2 µ s3 (t2 −t1)

(t2 −t1) =

a3m

where am = s/2 is the minimum energy ellipse semi-major axis [Battin(1999)]. The advantage of using λ and T derives from the fact that T is a function of a/am and λ alone, which allows to greatly simplify the taxonomy of possible Lambert’s problems. In Gooding’s words [Gooding(1990)], all the triangles having equal c/s ratio form a large equivalence class and can be described as

Fig. 1 Non-dimensional time of ﬂight curve for λ = −0.9 parametrized using a/am .

L-similar. For them, all Lambert solutions are the same in terms of a/am and T.

If we now plot the time of ﬂight given by Eq.(9) as a function of the ratio between the semi-major axis and the minimum energy ellipse semi-major axis, for a particular value of λ and for single and multiple revolution cases, we get Figure 1. It is evident how, in order to invert the time of ﬂight relation iterating over a/am, while possible, is not a good choice. To avoid these problems we follow Lancaster and Blanchard in some further derivations introducing the new quantities:

 cos α

cos β

2 cosh α

2 cosh β

x =

2 , y =

2 (14)

which imply:

1 −x2 = sin α 2 √

1 −x2 = sin β 2 λ

2 ,

2 (15)

x2 −1 = sinh α

x2 −1 = sinh β

1 −λ2(1 −x2). Using these relations it is possible to relate the auxiliary angles ϕ and ψ directly to x:

and y =

cos ϕ = xy −λ(1 −x2) cosh ϕ = xy + λ(x2 −1), sin ϕ = (y + xλ)

1 −x2

x2 −1 (16)

sinh ϕ = (y + xλ)

and,

cos ψ = xy + λ(1 −x2) cosh ψ = xy −λ(x2 −1), sin ψ = (y −xλ)

1 −x2

x2 −1 (17)

sinh ψ = (y −xλ)

1 −x2, cosh ϕ sinh ψ = (x −λy)

which allows to derive the relations cos ϕ sin ψ = (x−λy)

x2 −1 and thus have the following time of ﬂight equation valid in all cases:

T = 1 1 −x2

ψ + Mπ p

−x + λy

(18)

|1 −x2|

where we must set M = 0 in the case of hyperbolic motion where unbounded motion prevents complete revolutions to happen. The auxiliary angle ψ is computed using Eq.(17) by the appropriate inverse function and thus, the time of ﬂight evaluation is reduced to one only inverse function computation. Given the bounds on α, from the deﬁnition of x, we can see how x ∈[−1, ∞]. Also, x > 1 implies hyperbolic motion, while x < 1 elliptic motion. Since 1 −x2 = sin2 α 2 = s 2a = am a , we see how x = 0 corresponds to the minimum energy ellipse. Note that diﬀerent Lambert’s problems having identical λ values (i.e. same c/s), result in the same x, we then say that x is a Lambert invariant parameter.

Computing Eq.(18) in x = 0 we get:

1 −λ2 + Mπ = T00 + Mπ (19)

T(x = 0) = T0M = arccos λ + λ

where we have introduced T0 as the value of T in x = 0 and T00 as the value in the single revolution case M = 0.

When computing Eq.(18) in the single revolution case, a loss of precision is encountered due to numerical cancellation for values of x ≈1 where both 1−x2 and ψ tend to zero. In these cases we compute the time of ﬂight equation by series expansion using the elegant result from Battin [Battin(1999)] setting:

η = y −λx S1 = 1

2(1 −λ −xη) Q = 4

(20)

3 1F2(3, 1, 5 2, S1) 2T = η3Q + 4λη

where 1F2(a, b, c, d) is the Gaussian or ordinary hypergeometric function. This can be evaluated by direct computation of the associated hypergeometric se- ries. Noting that S1 →0 when x →1 the number of terms to retain in the series is small whenever the series is used in the neighbourhood of x = 1. Departing from Battin, we study the parabolic case substituting x = 1, y = 1 into Eq.(20) and thus obtaining the following remarkable expression:

T(x = 1) = T1 = 2

3(1 −λ3) (21)

relating the geometry of the triangle created by two diﬀerent observations of an object on a parabolic keplerian orbit to the non-dimensional time elapsed between them. It is also possible to derive the following formulas for the time of ﬂight derivatives:

(1 −x2) dT

dx = 3Tx −2 + 2λ3 x

y (1 −x2) d2T

dx + 2(1 −λ2) λ3

dx2 = 3T + 5x dT

(22)

y3 (1 −x2) d3T

dx3 = 7x d2T

dx2 + 8 dT

dx −6(1 −λ2)λ5 x

Fig. 2 Time of ﬂight curves parametrized using x for diﬀerent λ and M values.

which are valid in all cases (single and multiple revolutions, elliptic and hyper- bolic) except in λ2 = 1, x = 0 and x = 1, ∀λ. We then apply de l’Hˆopital rule to the ﬁrst of the above equations, and using the expression derived for T1 we are also able to ﬁnd the value of the derivative of the time of ﬂight curves in the case of a parabola:

= 2

5(λ5 −1) (23)

x=1

which is valid for M = 0. By direct substitution, one can also easily show:

= −2

x=0

A great advantage of the time of ﬂight equation in the form of Eq.(18) as derived by Lancaster and Blanchard [Lancaster and Blanchard(1969)] is in the low computational cost of computing T and its derivatives, up to the third order. Only one trigonometric (or hyperbolic) function inversion, two square roots and a few multiplications, divisions and sums are indeed necessary to compute these numerical values. Other approaches based on geomerical considerations or on a universal variables formulation are, at best, only able to match such a simple representation. We now summarize all the information relative to all possible Lambert problems in one single graph as done in Figure 2.

Fig. 3 Time of ﬂight curves (τ) parametrized using ξ for 30 λ values equally spaced in [−0.9, 0.9].

3 A new Lambert invariant variable

Let us consider the following new variables:

 log(1 + x), M = 0

ξ =

1−x), M > 0 , τ = log(T) (24)

log( 1+x

The domain of the time-of ﬂight curve is now extended to [−∞, ∞]. In the case of M = 0 the co-domain is also extended similarly. Let us study the resulting time of ﬂight equation τ(ξ, λ, M). In Figure 3 we plot τ against ξ for M = 0 and M = 1 and thirty equally spaced values of λ ∈[−0.9, 0.9]. In the case M = 0 the curves appear to have two asymptotes having negative inclination coeﬃcient. For the multiple revolution case (M > 0) the curves have two symmetric asymptotes. The new introduced parameter ξ is Lambert invariant according to Gooding’s deﬁnition [Gooding(1990)] as it essentially is a transformation of the Lambert invariant variable x. We study the diﬀerential properties of the new curves, we have:

 1 1+xdx, M = 0 2 1−x2 dx, M > 0 , dτ = 1

dξ =

T dT (25)

Substituting these relations into Eq.(22), after some manipulations we may derive the following hybrid expressions for the derivatives in the case M = 0:

dτ dξ = 1+x

d2τ dξ2 = (x+1)2

d2T

dτ dξ

dx2 + dτ

dξ −

(26)

2 

d3τ dξ3 = (1+x)3

d3T

d2τ dξ2 −dτ

+ d2τ

d2τ dξ2

dτ dξ

2 −dτ dξ

dξ2 −2 dτ

dξ +

dx3 +

Note that these expressions can be computed, sequentially, after Eq.(22). The

Fig. 4 Time of ﬂight curves parametrized using ξ for diﬀerent λ and M values.

following holds for the M = 0 case:

limξ→∞τ = −ξ + log(1 −λ|λ|) limξ→−∞τ = −3

4 √

2  (27)

2ξ + log   π

which describes the asymptotic behaviour of the time of ﬂight as visualized in Figure 3. The two asymptotes are thus revealed to have negative coeﬃcients −1 and −3/2. For the multirevolution cases the derivatives are found to be:

dτ dξ = 1−x2

2T dT

d2τ dξ2 = (1−x2)2

4T d2T

dx2 −x dτ

dτ dξ

dξ −

2! 

d3τ dξ3 = (1 −x2)3

8T d3T

d2τ dξ2 + xdτ

dτ

2x + dτ dξ

(28)

dx3 −

dξ +

−1 −x2

2 dτ dξ −xd2τ

d2τ dξ2

ξ2 −2dτ

again computable in cascade and the following asymptotic behavior can be derived:

  π+Mπ

8 

2ξ limξ→∞τ = log

limξ→−∞τ = log

2ξ (29)

  Mπ

+ 3

8 

revealing two symmetric asymptotes having inclination ±3/2. In Figure 4 we report the time of ﬂight curves in the new variables for diﬀerent values of λ and

M. The reader can then compare this ξ-τ plane to the x-T plane visualised in Figure 2.


## 4 Lambert Solver


A Lambert solver can be deﬁned as a procedure that returns, for a gravitational ﬁeld of strength µ, all the possible velocity vectors v1 and v2 along keplerian orbits linking r1, r2 in a transfer time T ∗. The ingredient of such an algorithm are, essentially a) the choice of a variable to iterate upon and thus invert the time of ﬂight curve, b) the iteration method, c) the starting guess to use with the iteration method and d) the reconstruction methodology to compute v1 and v2 from the value returned by the iterations. As we will detail, our Lambert solver a) iterates on the Lancaster-Blanchard variable x using b) a Householder iteration scheme c) feeded by initial guesses found exploiting the curve shape in the τ-ξ plane and the new analytical results found above. Eventually the velocity vectors are reconstructed following, again, the methodology proposed by Gooding [Gooding(1990)]. The ﬁnal pseudo-code of the proposed Lambert solver is reported in Algorithm 1, 2. Note how we detect the maximum number of revolutions Mmax at the beginning by computing Tmin in one case. All other cases (i.e. M <= Mmax) will not require the evaluation of a Tmin via an iterative procedure. By doing so we do not bound the roots (short and long period) and thus risk cases where the solution jumps between the long and short period branches. While this did not happen in our tests of the new routine, it is a possibility we are not safeguarding against.

The code, written in C++ and exposed to python, is made available as part of the open source project PyKEP from the European Space Agency github repository https://github.com/esa/pykep/. The ﬁnal algorithm is the ﬁnal result of many diﬀerent trials to exploit the newly found results detailed above, and in particular the T0, T1 expressions (and their derivatives) and the ξ-τ plane. It is worth reporting how one very robust set-up, not selected as our ﬁnal proposed algorithm, was that of iterating directly with a simple derivative free method (regula-falsi) on the ξ-τ plane using constant initial guesses (i.e xl = −0.7, xr = 0.7). We ended up choosing a diﬀerent set-up (Algorithm 1, 2) which turned up to be faster in our computational tests.

4.1 The Householder iterations

One of the main diﬀerences of the proposed Lamber solver with respect to previous work is the use of the Householder iterative scheme as a root ﬁnder for the time-of-ﬂight curves T(x)−T ∗= 0. Householder iterations are not used widely as the added computational eﬀort of computing higher order derivatives is not worth the gain whenever these request further function evaluations. In our case, as the derivatives computation is done using equations 22, House- holder iterations are able to provide a signiﬁcant beneﬁt. We report the exact

form used to implement the iterations as it is known how diﬀerent numeri- cal form can produce diﬀerent behaviours. After experimenting with diﬀerent implementations the following was used:

xn+1 = xn −f(xn) f ′2(xn) −f(xn)f ′′(xn)/2 f ′(xn)(f ′2(xn) −f(xn)f ′′(xn)) + f ′′′(xn)f 2(xn)/6

where f is, in our case, T(x)−T ∗and the derivatives are indicated as f ′, f ′′, f ′′′.


## 4.2 Initial Guess


To generate an initial guess for x we use the newly introduced ξ-τ variables and the values T0 and T1 as computed from Eq.(19) and Eq,(21). Our initial guess is obtained inverting the following linear approxmation to the time of ﬂight curves:

τ = cξ + d

where we vary the c and d values according to the value of τ and M.

4.2.1 Single revolution

Let us start form the single revolution case. Clearly, for high values of τ, we must set c = −1.0, while for low values c = −3/2 so that the asymptotic behavior derived in Eq.(27) is reproduced. We then consider the following piece-wise linear approximation:

τ = −3

2ξ + τ0, T ≥T0 τ = −ξ + log 2 + τ1, T < T1 τ = τ0 + ξ log 2(τ1 −τ0) T1 < T < T0

where τ1 = log T1 and τ0 = log T0. We have basically enforced the lines to pass through the points (x0, T0) and (x1, T1) and have the desired asymptotic behaviour.

The above relation is easily inverted and thus the following simple starter ξ0 is derived:

ξ0 = 2

3(τ0 −τ), T ≥T0 ξ0 = log 2 + τ1 −τ, T < T1 ξ0 = τ−τ0

τ1−τ0 log 2 T1 < T < T0

Transforming these relations back to the x-T plane we ﬁnd the following ex- pression for the starter x0:

  T0

 2

3 −1, T ≥T0

x0 =

(30)

x0 = 2 T1

T −1, T < T1

 T1

  T0

log2

−1 T1 < T < T0

x0 =

Fig. 5 Absolute errors introduced by the Gooding initial guess (left) and the proposed initial guess (right) for the single revolution M = 0 case. Each line correspond to a diﬀerent λ value ranging from -0.99 to 0.99

having an extremely low computational cost also in view of the fact log2 admits eﬃcient implementaions. We can improve the expression for the T < T1 case making use of the newly found result expressed in Eq.(23). We thus set

  T0

 2

3 −1, T ≥T0

x0 =

2 T1(T1−T )

x0 = 5

T (1−λ5) + 1, T < T1

 T1

  T0

log2

−1 T1 < T < T0

x0 =

where we enforced the derivatives and values at x = 1 and x = ∞. We report the error introduced by using these expressions in Figure 5 where we also show, for comparison, the same plot relative to the Gooding initial guess. The error is deﬁned by the diﬀerence between the initial guess computed for a given T ∗

and the actual value of x resulting in a time of ﬂight T ∗.

4.2.2 Multiple revolutions

For the multiple revolution case, assuming a solution exists, there are two possible values of x and thus we will need two starters. We obtain these by direct inversion of Eq.(29) that deﬁne the two asymptotes.

ξ0l = 2

3(log π+Mπ 8 −τ) ξ0r = 2

3(τ −log Mπ 8 )

The above equations may then be transformed back to the x-T plane so that the following simple starters are derived:

2 3 −1 ( Mπ+π

x0l = ( Mπ+π

8T )

2 3 +1

8T )

(31)

2 3 −1 ( 8T

x0r = ( 8T

Mπ)

2 3 +1

Mπ)

Algorithm 1 Lambert solver: inputs, r1 = [r11, r12, r13], r2 = [r21, r22, r23], t and µ Require: t > 0, µ > 0

c = r2 −r1 c = |c|, r1 = |r1|, r2 = |r2| s = 1

2 (r1 + r2 + c) ˆir,1 = r1/r1, ˆir,2 = r2/r2 ˆih = ˆir,1 ×ˆir,2

λ2 = 1 −c/s, λ =

λ2 if (r11r22 −r12r21) < 0 then

λ = −λ ˆit,1 = ˆir,1 ×ˆih, ˆit,2 = ˆir,2 ×ˆir,2 else

ˆit,1 = ˆih ×ˆir,1, ˆit,2 = ˆih ×ˆih end if T =

2µ s3 t xlist, ylist =ﬁndxy(λ, T)

2 , ρ = r1−r2 c , σ =

(1 −ρ2) for each x, y in xlist, ylist do

γ =

Vr,1 = γ[(λy −x) −ρ(λy + x)]/r1 Vr,2 = −γ[(λy −x) + ρ(λy + x)]/r2 Vt,1 = γσ(y + λx)/r1 Vt,2 = γσ(y + λx)/r2 v1 = Vr,1ˆir,1 + Vt,1ˆit,1 v2 = Vr,2ˆir,2 + Vt,2ˆit,2 end for

The above expressions approximate well the time of ﬂight curves as |x| →1. A great advantage of these expressions is that they do not make use of Tmin, xmin (i.e. the minimum of the time of ﬂight curve and its extremal value) which would require a distinct set of iterations to be found. We then avoid to pass to the root solver solution bounds at each M, at the risk of allowing, during the iterations, switches between short and long period solutions, a theoretical occurence, though, that was never encountered in our experimental tests.

5 New solver performances

To test the performances of our new algorithm we start by assessing its ac- curacy. We consider, for an assigned M, a random λ ∈[−0.999, 0.999] and a random xtrue ∈[−0.99, 3] (or xtrue ∈[−0.999, 0.999] whenever M > 0) and we compute the resulting time of ﬂight T. We then use Housholder iterations starting from the appropriate initial guess to ﬁnd back the x value. We ﬁnd that stopping the iterations whenever the diﬀerence between the x value com- puted at two successive iterations is less than 10−5 (10−8 whenever M > 0) is a good setting. We record, for each of such trials, the number of iterations made by the Householder method it, and the error deﬁned as ϵ = |xtrue −x|. This is repeated 1,000,000 times for M = 0 and then 100,000 times for each M = 1, 2, ...., 50. The result is shown in Figure 6. In the vast majority of

Algorithm 2 ﬁndxy(λ, T): computes all x, y for single and muti-rev solutions

Require: |λ| < 1, T < 0

Mmax = ﬂoor(T/π) T00 = arccos λ + λ

1 −λ2 if T < T00 + Mmaxπ and Mmax > 0 then

start Halley iterations from x = 0, T = T0 and ﬁnd Tmin(Mmax) if Tmin > T then

Mmax = Mmax −1 end if end if T1 = 2

3 (1 −λ3) compute x0 from Eq.(30) start Householder iterations from x0 and ﬁnd x, y while Mmax > 0 do

compute x0l and x0r from Eq.(31) with M = Mmax start Householder iterations from x0l and ﬁnd xr, yr start Householder iterations from x0r and ﬁnd xl, yl Mmax = Mmax −1 end while

cases we obtain an error < 10−13. Few cases have a slightly larger error (up to < 10−11) and these are mainly corresponding to multirevolution cases where T ≈Tmin. We also note, on the proximity of λ = 1 values, a distinct rise in the absolute error. This is due to the M = 0 case and the loss of precision in the computation of y from λ, a problem that can be avoided computing y directly from the problem geometry, but it is here deemed as not necessary. Looking then at the number of iterations, we compute the mean over all in- stances having the same M value. We obtain, for the single revolution case, an average of 2.1 iterations while, in the multiple revolution case, we get an aver- age of 3.3 iterations to convergence. Note how in these tests we do not ﬁnd a case where a switch occurs between the short period and long period solution during the root solving procedure. Such a switch would infact immediately appear in Figure 6 as a point with a large absolute error ϵ.

We then turn to the anaysis of our algorithm complexity with respect to the known Gooding algorithm, considered by many as the most accurate and eﬃcient Lambert solver up to date. First we note that in terms of accuracy, Gooding algorithm is comparable to ours. We then run a speed test. For the purose of this test we reimplement both our and Gooding algorithm in pure Python language (i.e. no C++ bindings) and we record the execution time to solve the same 100,000 randomly generated problems (using the same bounds as above). In the case M = 0, the proposed algorithm resulted to be faster by a factor 1.25, while in the multi revolution cases by a factor 1.5. This type of test is very sensitive to implementation details and to the underlying comput- ing architecture and even if we did our best to pay as much attention to them in both cases, we support our result with more general considerations. The main diﬀerence between our algorithm and Gooding’s is in the initial guess generation and in the iteration method. Gooding algorithm employs Halley

Fig. 6 Absolute error ϵ = |x −xtrue| of our Lambert solver as a function of λ. This is achieved, on average over the M = 0 cases, in 2 iterations

iterations, while we make use of Householder iterative scheme. While Halley’s method has a slighlty lower complexity and does not need to compute also the third derivative from Eq.(22), our iterative scheme reaches, in the case M = 0, a comparable accuracy in only 2 iterations on average compared to the 3 iterations needed for the Gooding case. For M > 0 the number of re- quired iterations is comparable in both cases but the initial Guess used in Gooding algorithm has, in general, a higher complexity as it makes use of a higher number of square roots and exponentiations. In the M > 0 case Good- ing initial guess also requires the determination of xmin, Tmin via a further Halley iterative scheme, while the initial guess we use does not make use of any particular value, while still allowing the Householder method to converge within a few iterations and in all cases. We must, though, note once more that as we do not compute xmin, Tmin we also cannot bound the solution during the root solver iterations and thus allow for the theoretical possibilty of a switch between short and long period solutions. Such a rare event never appeared in our extensive testing of the new routine

Finally, we measure the error also in terms of the computed terminal ve- locities by comparing all v2, returned by our Lambert solver, to the same values as computed via numerical propagation (using Lagrange coeﬃcients) from r1, v1. We do this by instantiating at random r1, r2 with each compo- nent in the range [−4, 4] and t ∈[0.1, 100]. For the purpose of this test we consider µ = 1 and we measure the norm of the resulting vector of the ve-

locity diﬀerence. Repeating this experiment for a total cumulative 10,000,000 Lambert’s Problems, an average error of 10−13 is obtained, with a maximum error measured to be 10−8.


## 6 Conclusion


We revisit Lambert’s problem building upon the results of Lancaster and Blan- chard and ﬁnding some new properties of the time of ﬂight curves. We pro- pose a new transformation of such curves able to further simplify the problem suggesting eﬃcient approximations to the ﬁnal solution. Using our results to design a new procedure to solve the Lambert problem we are able to build a low complexity algorithm that we ﬁnd able to provide accurate solutions in a shorter time when compared to the state of the art Gooding’s algorithm.


## References


Arora and Russell(2013). Arora N, Russell R (2013) A fast and robust multiple revolution

lambert algorithm using a cosine transformation. In: Paper AAS 13-728, AAS/AIAA Astrodynamics Specialist Conference 2 Arora and Russell(2010). Arora N, Russell RP (2010) A gpu accelerated multiple revolution

lambert solver for fast mission design. In: AAS/AIAA Space Flight Mechanics Meeting, San Diego, CA, No. AAS, pp 10–198 2 Bate et al(1971)Bate, Mueller, and White. Bate R, Mueller D, White J (1971) Fundamen-

tals of Astrodynamics. Courier Dover Publications 2 Battin(1999). Battin RH (1999) An introduction to the mathematics and methods of as-

trodynamics. AIAA 2, 5, 7 Gauss(1857). Gauss C (1857) Theory of the motion of the heavenly bodies moving about

the sun in conic sections: a translation of Carl Frdr. Gauss” Theoria motus”: With an appendix. By Ch. H. Davis. Little, Brown and Comp. 5 Gooding(1990). Gooding R (1990) A procedure for the solution of lambert’s orbital

boundary-value problem. Celestial Mechanics and Dynamical Astronomy 48(2):145–165 2, 5, 8, 11 Izzo(2006). Izzo D (2006) Lambert’s problem for exponential sinusoids. Journal of guidance,

control, and dynamics 29(5):1242–1245 1 Klumpp(1991). Klumpp A (1991) Relative Performance of Lambert Solvers 1: Zero Revo-

lution Methods. Tech. rep., Jet Propulsion Laboratory, No. 19930013090 2 Lancaster and Blanchard(1969). Lancaster E, Blanchard R (1969) A uniﬁed form of Lam-

bert’s theorem. National Aeronautics and Space Administration 2, 8 Luo et al(2011)Luo, Meng, and Han. Luo Q, Meng Z, Han C (2011) Solution algorithm of a

quasi-lamberts problem with ﬁxed ﬂight-direction angle constraint. Celestial Mechanics and Dynamical Astronomy 109(4):409–427 1 Parrish(2012). Parrish NL (2012) Accelerating lamberts problem on the gpu in matlab.

PhD thesis, California Polytechnic State University 2 Peterson and et al.(1991). Peterson G, et al (1991) Relative Performance of Lam- bert Solvers 1: Zero Revolution Methods. Advances in the Astronautical Sciences 136(1):14951510 2 Rauh and Parisi(2014). Rauh A, Parisi J (2014) Near distance approximation in astrody-

namical applications of lamberts theorem. Celestial Mechanics and Dynamical Astron- omy 118(1):49–74 1 Wagner and Wie(2011). Wagner S, Wie B (2011) GPU Accelerated Lambert Solution Meth-

ods for the Orbital Targeting Problem. Spaceﬂight Mechanics 140 2
