# Interplanetary Trajectory Planning with Monte Carlo Tree Search (Hennes & Izzo, IJCAI 2015)

> Source file: `IJCAI2015_MCTS_Interplanetary.pdf`

<!-- generated from PDF via pymupdf -->


Proceedings of the Twenty-Fourth International Joint Conference on Artificial Intelligence (IJCAI 2015)

Interplanetary Trajectory Planning

with Monte Carlo Tree Search

Daniel Hennes and Dario Izzo

European Space Agency Advanced Concepts Team Noordwijk, The Netherlands daniel.hennes@esa.int, dario.izzo@esa.int


## Abstract


of many internal parameters and often result in an inefﬁcient set-up.

Planning an interplanetary trajectory is a very com- plex task, traditionally accomplished by domain ex- perts using computer-aided design tools. Recent advances in trajectory optimization allow automa- tion of part of the trajectory design but have yet to provide an efﬁcient way to select promising plane- tary encounter sequences. In this work, we present a heuristic-free approach to automated trajectory planning (including the encounter sequence plan- ning) based on Monte Carlo Tree Search (MCTS). We discuss a number of modiﬁcations to traditional MCTS unique to the domain of interplanetary tra- jectory planning and provide results on the Rosetta and Cassini-Huygens interplanetary mission design problems. The resulting heuristic-free method is found to be orders of magnitude more efﬁcient with respect to a standard tree search with heuristic- based pruning which is the current state-of-the art in this domain.

A relatively recent method to tackle extremely complex combinatorial problems is that of the Monte Carlo Tree Search (MCTS) paradigm. Born in the context of two-person zero-sum games with perfect information; in MCTS a node is evaluated by averaging the ﬁnal outcome of several random simulations. Four steps, namely: selection, expansion, simu- lation, and back-propagation, are performed iteratively up to when a stopping criteria is reached. Many papers have stud- ied MCTS and applied its modiﬁcations and variants to dif- ferent domains with great success; for an excellent overview see [Browne et al., 2012].

In this paper we propose a heuristic-free approach based on MCTS to tackle the complex problem of interplanetary trajectory planning, and in particular the planetary encounter selection problem. We start by giving a brief background on the interplanetary trajectory design problem and MCTS. Next, we look at trajectory design as a planning task, deﬁn- ing the possible actions and their relation to the ﬁnal trajec- tory. We then describe a number of modiﬁcations to tradi- tional MCTS, unique to the domain of interplanetary trajec- tory planning. We discuss our experimental set-up, in par- ticular parameter search and runtime analysis. The resulting heuristic-free method is found to be orders of magnitude more efﬁcient with respect to standard tree search with heuristic- based pruning which is the current state-of-the art in this domain [Longuski and Williams, 1991; Heaton et al., 2002; Petropoulos et al., 2000].

1 Introduction Interplanetary trajectory optimization holds its most chal- lenging aspect in its combinatorial part, that is the selection of the planetary encounters.

Tree searches with heuristic-based pruning, implementing problem knowledge, are the state-of-the-art in the aerospace industry for tackling these problems. A notable example is the software STOUR in use at NASA, Jet Propulsion Lab- oratory [Longuski and Williams, 1991] which targets the automated design of trajectories with multiple ﬂy-bys and has been used in several important mission design works [Heaton et al., 2002; Petropoulos et al., 2000]. Some at- tempts have been made in the last decade to advance the state-of-the-art by proving the use of advanced combinato- rial search paradigms such as Ant Colony Optimization [Ce- riotti and Vasile, 2010], genetic algorithms [Deb et al., 2007; Gad and Abdelkhalik, 2011; Izzo et al., 2014], tree search strategies [Izzo et al., 2014; Petropoulos et al., 2014], or bi- level optimization setups [Englander, 2013]. While some- what successful, these advanced methods all make use of problem knowledge to deﬁne heuristics and their application to different data sets or domains requires substantial tuning


## 2 Background


2.1 Interplanetary trajectories

We begin with a brief outline of the fundamentals of trajec- tory design. Even today’s most powerful launch systems are not able to send a spacecraft on a direct trajectory to target bodies in the outer solar system. Therefore, most interplan- etary missions require a well designed trajectory that guides the spacecraft through a number of gravity assist maneuvers, also called ﬂy-bys. Each ﬂy-by provides the spacecraft with a gravitational kick by “stealing” a small amount of the planet’s orbital energy. A sequence of carefully planned and executed ﬂy-bys allows the spacecraft to save propellant (or time) and enables trajectories otherwise impossible.

769

tive velocity at Earth ˜v−

In this paper we model an interplanetary spacecraft tra- jectory using a multiple gravity assist impulsive propulsion model (known as the MGA model [Izzo et al., 2007]), which is a very good preliminary approximation for a spacecraft equipped with chemical propulsion featuring a high thrust ca- pability. In the MGA model, the spacecraft is assumed to be able to perform powered ﬂy-bys; at each gravity assist ma- neuver an impulse, modeled as a discontinuity ∆V in the spacecraft velocity, can be used to correct the ﬂy-by geom- etry or decelerate the spacecraft to rendezvous with a target body. Impulses at any other point of the trajectory, i.e. deep space maneuvers, are not considered in this work. Using this model, a trajectory T is deﬁned by a pair (P, ˜T) where P is an ordered set of cardinality N containing the planetary en- counter sequence (e.g. P = {Earth, Venus, Jupiter}), and ˜T is an ordered set of cardinality N containing the epochs of the planetary encounters (e.g. ˜T = {t0, t1, t2}). It is conve- nient for the deﬁnition of time grids to use, instead of ˜T, the ordered set T containing, rather then the epochs, the various time of ﬂights (e.g. T = {t0, t1−t0, t2−t1} = {t0, T1, T2}).

0 :

0, |˜v− 0 | −vLS .

∆VLaunch = max

At the ﬁnal encounter, a maneuver is performed to capture the spacecraft in the gravitational ﬁeld of the target body. We compute this as ∆Vrndvz = |˜v+

N−1|, a contribution, called rendezvous maneuver, necessary to match the velocity of the target body. A generic MGA trajectory T = (P, T) is then associated to a required ∆Vtot deﬁned by:

∆Vi + ∆Vrndvz .

∆Vtot = ∆VLaunch +

i=1..N−2

Finding the correct planetary sequence P and time schedule T that allow for the minimization of this ∆Vtot is the problem we tackle and solve in the remainder of this paper.

2.2 Monte Carlo Tree Search Monte Carlo Tree Search (MCTS) is a technique widely ap- plicable in domains that require sequential decision mak- ing, including game-tree search and planning problems. The MCTS paradigm combines informed tree search with the gen- erality of Monte Carlo simulations. Although, MCTS ex- ist in many variants, all are based on the concept of incre- mentally building an internal tree to inform its search policy. MCTS algorithms are any-time algorithms that repeat the fol- lowing four basic steps until the computational budget is de- pleted [Chaslot et al., 2008]:

Given a trajectory T = (P, T), we may evaluate the cumu- lative change of velocity (total ∆V ) required to ﬂy it. This quantity is of fundamental importance in interplanetary tra- jectory design, as each required velocity change relates di- rectly to the propellant mass via the Tsiolkovsky equation, ∆V = Ispg0 ln (mi/mf), where mf is the spacecraft mass at the end of the maneuver and mi its mass at the beginning. Trivially, mf = mi −mp where mp is the propellant mass spent.

1. Selection: Starting from the root a selection policy is deployed to descend through the tree while balancing exploration and exploitation. 2. Expansion: Once the tree reaches a leaf node, the state is advanced by performing a random available action and the resulting state is added as a new node to the tree. 3. Simulation: A Monte Carlo simulation is run with ran- dom action selection. Heuristic knowledge can be used to give higher weight to promising actions. 4. Back-propagation: Once a ﬁnal state is reached, the value is back-propagated upwards trough the search tree and each node selected in step 1 is updated accordingly. Let us now discuss a popular choice of MCTS for planning problems, Upper Conﬁdence bounds for Trees (UCT) [Koc- sis and Szepesv´ari, 2006], in further detail. UCT is based on the Upper Conﬁdence Bound (UCB) [Auer et al., 2002] selection strategies for multi-armed bandit problems. The multi-armed bandit problem is a classical toy problem ad- dressing the exploration-exploitation dilemma. In particu- lar, Auer et. al [Auer et al., 2002] propose a policy UCB1 that has a bounded regret for arbitrary reward distributions with support in [0, 1] after any number of plays (i.e. in ﬁnite-time). The same paper also discusses various other selection policies, most notably ϵ-greedy and UCB1-Tuned. The authors make the following observations: an optimally tuned ϵ-greedy policy performs almost always best; UCB1- Tuned performs comparably to a well-tuned ϵ-greedy policy but without a proven regret bound. The UCB1-Tuned policy takes into account the measured variance of rewards and is thus less sensitive to the reward distribution than UCB1.

We start by computing the positions ri and velocities vi of the planets at the encounter epochs ∀i ∈[0..N −1]. For this computation we use the analytical planet ephemerides deﬁned by NASA / Jet Propulsion Laboratory1. Given two position vectors of two sequential encounters ri−1 and ri, and the time of ﬂight Ti, we can then determine the space- craft’s absolute velocities v+

i−1 and v−

i at the encounters (i.e. ∀i ∈[1..N −1]) by solving Lambert’s problem [Izzo, 2014]2. We may then compute, at each planet, the spacecraft relative velocities before and after the encounter ˜vi

−= v−

i −vi, ˜vi

+ = v+

i −vi. Two Lambert legs, e.g. Earth-Venus and Venus-Jupiter, can then be “patched” together by calculating the required ∆V to patch ˜v−,+ at Venus. The required ∆V maneuver, computed accounting for the hyperbolic ﬂy-by tra- jectory, depends on the the magnitude of the relative arrival and departure velocities at Venus but also on the angle β be- tween the relative velocities [Izzo et al., 2007].

In addition to the various ∆Vi computed at the encounters, the contributions at launch and arrival must be considered. The relative departure velocity at launch is either fully or par- tially supplied by the launch system. We thus discount the launcher’s maximum velocity vLS from the departure rela-

1The approximated ephemerides were used as deﬁned in http: //ssd.jpl.nasa.gov/?planet pos [accessed November 2014]

2In this work we only consider 0-revolution Lambert solutions. The extension of our methods to multiple revolution is straight for- ward.

770

3.1 Ephemeris grid The UCB selection policy used in UCT is based on ﬁnite multi-armed bandits problems; as such the action space must be a set of discrete choices. However, the initial action t0 and the TOF-actions Ti are continuous in nature. A straight- forward solution is to use a regular tiling of the time domain, e.g. grid points every 5 days. Although, regular-tiling is often used as a discretization technique of the state, action and time domain in robotics, it is highly inefﬁcient for Keplerian mo- tions. Planets in the inner solar system have orbital periods of just 88 days (Mercury) to 687 days (Mars), while outer plan- ets take 12 years (Jupiter) to 29 years (Saturn) to complete an orbit. We thus deﬁne a 1D grid for each body based on its orbital period τi and a grid resolution parameter l [deg]:

UCT follows the MCTS approch outlined above and de- ploys the following selection policy:

ln n

¯Xi + Cp

, (1)

arg min

where ¯Xi is the estimated reward for child i (or action i), n is number of times the current node has been selected, and ni is the number of times the child i has been updated. The parameter Cp has been introduced to balance exploration- exploitation behavior. If Cp = 1 √

2, then Equation (1) is equivalent to the UCB1 policy as introduced in [Auer et al., 2002]. The ϵ-greedy policy is computationally less demand- ing and thus of interest in complex planning problems with many tree updates. Equation (2) shows a slightly modiﬁed version of ϵ-greedy as introduced in [Sabharwal et al., 2012]:

360◦, j ∈N 

t0 + j lτi

. (5)

G =

¯Xi + ϵn

. (2)

arg min

The action set A(t0) is a subset of GEarth in agreement with the launch window; each TOF action set A(Ti) is a subset of GPi such that t0 + Pi−1

Both selection policies (1) and (2) require tuning of an exploration–exploitation parameter, Cp and ϵ respectively. The UCB1-Tuned policy uses the measured variance of re- wards:

j=1 Tj + Ti falls on GPi in agreement with the bounds on the time of ﬂight. This ensures that all ephemeris calculations, i.e. the calculation of the location r and velocity v, of a body are aligned with its grid. The com- plexity of the planning problem increases exponentially with decreasing grid resolution parameter l as shown in Figure 1.

ln n

min{1

¯Xi +

4, Vi}, (3)

arg min

where Vi is the variance for child i:

ni X

Vi = (1

2 ln n ni

2 ln n ni

2 +

i,j) −¯X2

. (4)

i +

= σi

3.2 Selection policy The UCB1 and UCB-Tuned policies require a reward dis- tribution with support in the interval [0, 1]. The evaluation of a trajectory is the sum of all required ∆V maneuvers in- cluding ﬁnal rendezvous and thus in the half-closed interval ∆Vtot ∈[0, ∞). We thus map ∆V tot to the reward for simu- lation j as Xj ∈[0, ] as follows:

j=1

All three variants have been applied as selection policies in MCTS variants.

3 MCTS for Trajectory Planning In order to apply UCT to interplanetary trajectory design, we must transcribe the problem of ﬁnding an interplanetary tra- jectory T = (P, T) as a planning task. The initial state (i.e. root of the tree) is pre-launch at Earth and the ﬁrst action is selecting the departure date t0 ∈[t0, t0] where t0 and t0 deﬁne the boundaries of the launch window. The consec- utive moves alternate between selecting the next planetary encounter Pi ∈{Venus, Earth, Mars, ...}, and time of ﬂight Ti ∈[T i, T i] where the limits T i and T i are chosen looking at the orbital periods τi−1, τi of Pi−1 and Pi and setting the wide bounds T i = 0.1∗(τi+τi−1), T i = 2∗(τi+τi−1). The action sequence: {t0 = 5110, P1 = Venus, T1 = 150, P2 = Jupiter, T2 = 2000} thus deﬁnes a trajectory departing from Earth3 at time 5110 MJD2000 (Modiﬁed Julian Day 2000) with two Lambert legs: from Earth to Venus in 150 days and from Venus to Jupiter in 2000 days. Contrary to the usual ap- proach for trajectory design, this problem description allows us to simultaneously tackle the selection of the planetary en- counter sequence and the timing of ﬂyby. As most of the search space is spanned by very costly actions (resulting in infeasible trajectories requiring unrealistic propellant quanti- ties) we also consider a threshold on the ∆V of 10 [km/s], so that if at any node the cumulative ∆V exceeds this threshold the state is considered terminal.

0, ∆Vmax −∆Vtot,j ∆Vmax

. (6)

Xj = max

Furthermore, we redeﬁne the node value estimator ¯X in Equations (1), (2), and (3) as follows:

ni max

¯Xi =

j=1 Xj , (7)

where Xj is the reward of simulation j. For a given action sequence, each of the Lambert problems has a unique solu- tion. As such, the patching velocities and the launch and ren- dezvous ∆V calculations are deterministic in nature. We can thus use the max-estimator instead of the average over Xj as each Xj gives a guaranteed lower bound on true value of node j. In previous work, the “max-style” estimator is either used in combination with the average estimator for MCTS in stochastic single-player games [Schadd et al., 2008] or as the sole node estimator in deterministic domains [Sabharwal et al., 2012].

3.3 Expansion In MCTS, the node expansion occurs at a leaf node after de- scending through the internal tree. A previously untried ac- tion is chosen at random and the corresponding node is added

3P0 = Earth is implied.

771

109

108

107

Lambert legs

106

105

104

Cassini - t0 ﬁxed

Cassini Rosetta - t0 ﬁxed

103

Rosetta

102

90◦ 45◦ 22.5◦ 11.25◦

Ephimeris grid resolution

Figure 1: Problem complexity as a function of grid size.

Table 1: Global minima for l = 11.25◦.

Figure 2: Example of a possible Rosetta mission trajectory.

Problem ﬁxed t0 full Cassini-Huygens 6.760 km/s 6.571 km/s Rosetta 6.498 km/s 5.887 km/s

3.5 Flat Monte Carlo In addition to the UCT with the various selection policies we establish a baseline by including experiments with Flat Monte Carlo search. Flat Monte Carlo does not gradually build a tree but rather performs simulations starting from the root until the computational budget is depleted, at which point the best encountered solution is returned [Browne et al., 2012].

to the tree, at which point the Monte Carlo simulation starts. As stated above, the evaluation of a simulation step provides a guaranteed bound. As such there is no need to re-evaluate the same trajectory again either in part or in whole. We thus expand the internal tree by all nodes encountered during the Monte Carlo simulation.


## 4 Experimental Evaluation


4.1 Problem set We evaluate our approach on two well-known missions: Cassini-Huygens and Rosetta. Cassini-Huygens is a joint NASA/ESA mission launched in 1997 sent to Saturn. The spacecraft Cassini arrived at its destination in 2004; it has since successfully deployed the Huygens lander to Saturn’s moon Titan and studied many of Saturn’s satellites.


## 3.4 Contraction


In addition to the four steps of MCTS (i.e, selection, ex- pansion, simulation, and backpropagaion), we introduce a ﬁfth step: contraction. When MCTS is applied to game-tree search, the computational budget is usually relatively limited and thus it is with low probability that the internal tree reaches the depth of the full search tree (i.e., a leave of the internal tree is a ﬁnal state). This is especially the case in the early phase of the game; later in the game, when the internal tree reaches ﬁnal states, end-game databases have been proven to be more efﬁcient [Browne et al., 2012]. Trajectory planning problems span a very broad tree, with a branching factor as high as 500 and limited depth, i.e. a trajectory rarely includes more than 5-10 ﬂy-bys and thus a search depth of 11-21. In addition, we are not concerned with executing the expected best move at the root node, as the case in traditional MCTS, but rather in the full trajectory. The modiﬁed expansion step and a rather broad but shallow tree, allow our algorithm to grow an internal tree that indeed reaches ﬁnal nodes. Any subtree of the internal tree that fully covers the corresponding problem search space (i.e., each child is played at least once and all leaves are ﬁnal states) can be ignored by further search iterations and is thus removed from the internal tree.

Rosetta is an ESA-operated mission launched in 2004. Rosetta arrived at its destination, comet 67P/Churyumov- Gerasimenko, in August 2014. It is the ﬁrst mission in history to rendezvous with a comet, escort it as it orbits the Sun, and successfully deploy a lander probe, named Philae, for a triple landing on the comet’s surface.

We transcribe the two missions as planning problems fol- lowing the approach outlined in Section 3. For both missions we deﬁne two variants, the ﬁxed t0 variant and the full vari- ant. The ﬁxed-t0 variant, ﬁxes the ﬁrst action of the planning problem to a launch date close to the actual mission launch; t0 = 1551.31 MJD2000 for Rosetta and t0 = −787.53 MJD2000 for Cassini-Huygens. The full variant includes the ﬁrst action within a 200 day and 365 day launch window for Cassini-Huygens and Rosetta respectively. An example of a Rosetta trajectory as described by our trajectory planning model is shown in Figure 2.

The complexity in terms of Lambert legs is shown in Fig- ure 1. The graph shows the number of Lambert legs re-

772

14.0

14.0

13.0

13.0

12.0

12.0

11.0

11.0

∆v [km/s]

∆v [km/s]

10.0

10.0

9.0

9.0

8.0

8.0

7.0

7.0

6.0

6.0

0.001 0.01 0.1 1 10 ǫ

0.001 0.01 0.1 1 10 100 ǫ

(a) ϵ-greedy, N=50,000

(b) ϵ-greedy, N=100,000

14.0

14.0

13.0

13.0

12.0

12.0

11.0

11.0

∆v [km/s]

∆v [km/s]

10.0

10.0

9.0

9.0

8.0

8.0

7.0

7.0

6.0

6.0

0.001 0.01 0.1 1 10 Cp

0.001 0.01 0.1 1 10 Cp

(c) UCB1, N=50,000

(d) UCB1, N=100,000

Figure 3: Parameter search for the ﬁxed-t0 Cassini-Huygens problem with l = 11.25◦grid resolution.

quired to exhaustively search the problem space by depth- ﬁrst search. Table 1 lists the minimum ∆V solutions for both problems found by exhaustive search with the ﬁnest grid res- olution l = 11.25◦.

to conduct the performance evaluation.

4.3 Performance evaluation To investigate the performance of the different selection policies, we determine the expected runtime of the search. The expected runtime E(RT) is computed with respect to the number of Lambert legs. In particular, the formula- tion in [Auger and Hansen, 2005] has been followed, where E(RT) is deﬁned as:

4.2 Parameter search The UCB-1 (see Equation (1)) and ϵ-greedy (see Equation 2) selection policies require one parameter choice each. Per- formance is expected to greatly depend on careful tuning of these parameters and we can not rely on “classical” choices as no prior work has been done in the intersection of MCTS and interplanetary trajectory planning. Therefore, we deploy a parameter search, inspired by the methodology described in [Kuipers et al., 2013]. We sample 4000 parameter in- stances uniformly on a logarithmic scale. For each param- eter instance, one run of UCT with the selected policy is per- formed until the computational budget of N Lambert legs is depleted. In addition to the approach in [Kuipers et al., 2013], we ﬁt a log-normal distribution to the parameter samples that result in runs that reached the minimum ∆V solution. The mean of the ﬁtted distribution is used as a parameter choice

E(RT) = 1 −ps

N + E(RTs) ,

where E(RTs) is the expected number of Lambert legs for a successful trial, ps is the probability of convergence to the target value, and N is the budget for a trail. The target value is deﬁned as ∆Vt = min ∆V + ϵ, where min ∆V is the min- imum ∆V solution and ϵ = 50 m/s a convergence threshold.

5 Results Figure 3 shows the result of the parameter search in the Cassini-Huygens ﬁxed-t0 problem for 4000 sample points.

773

Table 4: Expected runtime for full problems; budget per run N=100,000.

Table 2: Mean values for ﬁtted log-normal distributions; pa- rameters in bold are used for further simulations.

Problem ps RTs E(RT) Cassini-Huygens 0.00025 64,607 399,964,607 Rosetta 0.23588 68,602 392,543

Cassini-Huygens ﬁxed t0 full Lambert Legs 50K 100K 100K 1M ϵ-greedy (ϵ) 0.0110 0.0125 — 0.0139 UCB1 (Cp) — 0.5730 — —

to what is commonly done in most approaches to trajectory design, the sequence of planetary encounters (i.e. the com- binatorial part of the problem) was not ﬁxed a priori. The ﬁnal result for a generic mission is a number of trajectory op- tions, each comprised of an encounter sequence P and a time line T.

Rosetta ﬁxed t0 full Lambert Legs 50K 100K 100K 1M ϵ-greedy (ϵ) 0.0146 0.0203 0.0406 — UCB1 (Cp) 0.4427 0.6078 1.8454 —

With respect to a depth-ﬁrst search (also using a pruning threshold of 10 km/s), MCTS makes use of orders of mag- nitude less Lambert leg computations to ﬁnd the global opti- mal solution. The real strength of the approach, though, lies in its ability to ﬁnd very good solutions with a very limited computational budget. Best–ﬁrst search, such as A∗or beam search, would only be as successful as the employed future– cost heuristic which is not readily available in planetary tra- jectory planning; whereas our algorithm is heuristic–free.

Table 3: Expected runtime for problems with ﬁxed launch date t0; budget per run N=50,000.

Cassini-Huygens ps RTs E(RT) ϵ-greedy 0.07550 33,446 645,697 UCB1 0.00980 19,305 5,097,510 UCB1-Tuned 0.0 — ∞ Flat Monte Carlo 0.0 — ∞

As such, our MCTS-based approach seems to be a very promising candidate to substitute current algorithms aimed at helping mission designers to identify good planetary en- counter options for preliminary mission design. As shown in the case of the Cassini-Huygens mission, our approach was able to ﬁnd the best sequence of encounters and a time line that is very close to the one ﬂown by the real mission. As ex- pected, in case of the Rosetta mission the suggested trajectory options were incomplete. The ﬂy-by sequence actually used in the Rosetta mission was not found; although other good options were located by MCTS. This is due to the fact that employed MGA trajectory model, while computationally ad- vantageous, is not capable to describe ∆V -EGA (∆V - Earth Gravity Assist) maneuvers [Sims et al., 1997]. These maneu- vers are critical to the use of the ﬂy-by sequence E–E–M–E– E–67P which was adopted for the Rosetta mission. This is also the reason why ﬁxing the launch date produces a more difﬁcult search problem than the open launch window. In the open launch window, a multitude of solutions with different ﬂyby sequences can be found that score within the min ∆Vt threshold.

Rosetta ps RTs E(RT) ϵ-greedy 0.39100 31,330 109,207 UCB1 0.00850 33,652 5,866,005 UCB1-Tuned 0.00075 21,503 66,638,169 Flat Monte Carlo 0.0 — ∞

The runs that reached minimum ∆V values are highlighted in red. The mean values of the log-normal distribution are re- ported in Table 2. We see a number of “solution-bands”, most dominantly around 9 km/s; too little or too much exploitation traps the search in local minima. The experiment presented in Figure 3 (c) resulted in less than 10 successful runs, thus an estimate for Cp was not attempted. Table 2 lists parame- ter choices for the Cassini-Huygens and Rosetta missions as identiﬁed by the parameter search and distribution ﬁtting. For the full Cassini-Huygens problem, a parameter estimate for ϵ could only be established for runs with N=1,000,000.

Our results are consistent with past ﬁndings [Auer et al., 2002; Browne et al., 2012] with respect to the effectiveness of a well-tuned ϵ-greedy policy. Furthermore, our results show that UCB1-Tuned does not outperform UCB1. We employ the “max-style” node value estimator for all selection po- lices. UCB1-Tuned uses a measured variance to scale the up- per bound which seems to be ineffective in combination with this max-estimator. We aim to address this issue in future work by using the variance of child node values as opposed to measured variance of the back-propagated values. In addi- tion, the use of more sophisticated trajectory models and the inclusion of a second objective to score trajectories (i.e. total time of ﬂight) are interesting future extensions of this work. We expect that the application of the MCTS paradigm would produce reliable and fast results also in these cases.

Table 3 and Table 4 show the expected runtime for both problems with Lambert leg budget N=50,000 for ﬁxed-t0 and Lambert leg budget N=100,000 for the full problems. The ϵ-greedy selection policy performs overall best, UCB1 is one order of magnitude worse in the ﬁxed-t0 case and fails to reach the minimum solution in the full problem. UCB1- Tuned performs worse than UCB1. Only well-tuned ϵ-greedy selection found the minimum solution given the limited com- putational budget.


## 6 Discussion


We have presented an approach to transcribe the preliminary- phase of an interplanetary trajectory design as a planning problem to which we applied the MCTS paradigm. Contrary

774


## References [Auer et al., 2002] Peter Auer, Nicolo Cesa-Bianchi, and


chine Learning: ECML 2006, pages 282–293. Springer, 2006. [Kuipers et al., 2013] Jan Kuipers, Aske Plaat, JAM Ver-

Paul Fischer. Finite-time analysis of the multiarmed bandit problem. Machine learning, 47(2-3):235–256, 2002. [Auger and Hansen, 2005] A. Auger and N. Hansen. Perfor-

maseren, and H Jaap van den Herik. Improving mul- tivariate horner schemes with monte carlo tree search. Computer Physics Communications, 184(11):2391–2395, 2013. [Longuski and Williams, 1991] James M Longuski and Steve N Williams. Automated design of gravity-assist trajectories to mars and the outer planets. Celestial Mechanics and Dynamical Astronomy, 52(3):207–220, 1991. [Petropoulos et al., 2000] Anastassios E Petropoulos, James M Longuski, and Eugene P Bonﬁglio. Trajectories to jupiter via gravity assists from venus, earth, and mars. Journal of Spacecraft and Rockets, 37(6):776–783, 2000. [Petropoulos et al., 2014] Anastassios E. Petropoulos, Eu-

mance evaluation of an advanced local search evolution- ary algorithm. In Congress on Evolutionary Computation (CEC 2005), volume 2, pages 1777–1784, Piscataway, NJ, USA, 2005. IEEE Press. [Browne et al., 2012] Cameron B Browne, Edward Pow-

ley, Daniel Whitehouse, Simon M Lucas, Peter I Cowl- ing, Philipp Rohlfshagen, Stephen Tavener, Diego Perez, Spyridon Samothrakis, and Simon Colton. A survey of monte carlo tree search methods. Computational Intelli- gence and AI in Games, IEEE Transactions on, 4(1):1–43, 2012. [Ceriotti and Vasile, 2010] Matteo Ceriotti and Massimil-

gene P. Bonﬁglio, Daniel J. Grebow, Try Lam, Jeffrey S. Parker, Juan Arrieta, Damon F. Landau, Rodney L. An- derson, Eric D. Gustafson, Gregory J. Whiffen, Paul A. Finlayson, and Jon A. Sims. GTOC5: Results from the Jet Propulsion Laboratory. Acta Futura, 8:21–27, 2014. [Sabharwal et al., 2012] Ashish Sabharwal, Horst Samu- lowitz, and Chandra Reddy. Guiding combinatorial opti- mization with uct. In Integration of AI and OR Techniques in Contraint Programming for Combinatorial Optimzation Problems, pages 356–361. Springer, 2012. [Schadd et al., 2008] Maarten PD Schadd, Mark HM Winands, H Jaap Van Den Herik, Guillaume MJ-B Chaslot, and Jos WHM Uiterwijk. Single-player monte- carlo tree search. In Computers and Games, pages 1–12. Springer, 2008. [Sims et al., 1997] Jon A Sims, James M Longuski, and An-

iano Vasile. MGA trajectory planning with an ACO- inspired algorithm. Acta Astronautica, 67(9):1202–1217, 2010. [Chaslot et al., 2008] Guillaume Chaslot, Sander Bakkes,

Istvan Szita, and Pieter Spronck. Monte-carlo tree search: A new framework for game ai. In AIIDE, 2008. [Deb et al., 2007] Kalyanmoy Deb, Nikhil Padhye, Ganesh

Neema, and V Adimurthy. Interplanetary trajectory optimization with swing-bys using evolutionary multi- objective optimization. Lecture Notes in Computer Sci- ence, 4683:26–35, 2007. [Englander, 2013] Jacob Englander. Automated trajectory planning for multiple-ﬂyby interplanetary missions. PhD thesis, University of Illinois at Urbana-Champaign, 2013. [Gad and Abdelkhalik, 2011] Ahmed Gad and Ossama Ab-

delkhalik. Hidden genes genetic algorithm for multi- gravity-assist trajectories optimization. Journal of Space- craft and Rockets, 48(4):629–641, 2011. [Heaton et al., 2002] Andrew F Heaton, Nathan J Strange,

drew J Staugler. V8 leveraging for interplanetary mis- sions: Multiple-revolution orbit techniques. Journal of Guidance, Control, and Dynamics, 20(3):409–415, 1997.

James M Longuski, and Eugene P Bonﬁglio. Automated design of the europa orbiter tour. Journal of Spacecraft and Rockets, 39(1):17–22, 2002. [Izzo et al., 2007] Dario Izzo, Victor M Becerra, DR Myatt,

Slawomir J Nasuto, and J Mark Bishop. Search space pruning and global optimisation of multiple gravity assist spacecraft trajectories. Journal of Global Optimization, 38(2):283–296, 2007. [Izzo et al., 2014] Dario Izzo, Lu´ıs F. Sim˜oes, Chit Hong

Yam, Francesco Biscani, David Di Lorenzo, Bernardetta Addis, and Andrea Cassioli. GTOC5: Results from the European Space Agency and University of Florence. Acta Futura, 8:45–55, 2014. [Izzo, 2014] Dario Izzo. Revisiting Lamberts problem. Ce-

lestial Mechanics and Dynamical Astronomy, pages 1–15, 2014. [Kocsis and Szepesv´ari, 2006] Levente Kocsis and Csaba

Szepesv´ari. Bandit based monte-carlo planning. In Ma-

775
