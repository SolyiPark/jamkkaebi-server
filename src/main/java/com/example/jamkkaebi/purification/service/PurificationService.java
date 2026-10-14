package com.example.jamkkaebi.purification.service;

import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import com.example.jamkkaebi.global.response.FieldError;
import com.example.jamkkaebi.global.time.GameClock;
import com.example.jamkkaebi.purification.config.DifficultyParams;
import com.example.jamkkaebi.purification.config.PurificationProperties;
import com.example.jamkkaebi.purification.domain.NextAction;
import com.example.jamkkaebi.purification.domain.PlayResult;
import com.example.jamkkaebi.purification.domain.PlaySession;
import com.example.jamkkaebi.purification.domain.SessionStatus;
import com.example.jamkkaebi.purification.dto.request.PurificationResultRequest;
import com.example.jamkkaebi.purification.dto.request.PurificationStartRequest;
import com.example.jamkkaebi.purification.dto.response.BoardView;
import com.example.jamkkaebi.purification.dto.response.DifficultyListResponse;
import com.example.jamkkaebi.purification.dto.response.DifficultyView;
import com.example.jamkkaebi.purification.dto.response.PurificationResultResponse;
import com.example.jamkkaebi.purification.dto.response.PurificationStartResponse;
import com.example.jamkkaebi.purification.repository.PlaySessionRepository;
import com.example.jamkkaebi.relic.domain.AwakeningStage;
import com.example.jamkkaebi.relic.domain.Clarity;
import com.example.jamkkaebi.relic.domain.Difficulty;
import com.example.jamkkaebi.relic.domain.EntryRoute;
import com.example.jamkkaebi.relic.domain.Phase;
import com.example.jamkkaebi.relic.domain.RelicMaster;
import com.example.jamkkaebi.relic.domain.RevealLevel;
import com.example.jamkkaebi.relic.domain.UserRelic;
import com.example.jamkkaebi.relic.dto.response.BuildingView;
import com.example.jamkkaebi.relic.dto.response.GaugeView;
import com.example.jamkkaebi.relic.dto.response.RelicCard;
import com.example.jamkkaebi.relic.repository.UserRelicRepository;
import com.example.jamkkaebi.relic.service.BuildingAwardService;
import com.example.jamkkaebi.relic.service.ClarityPolicy;
import com.example.jamkkaebi.relic.service.EraCrystalWallet;
import com.example.jamkkaebi.relic.service.GrowthPolicy;
import com.example.jamkkaebi.relic.service.RelicOwnership;
import com.example.jamkkaebi.user.domain.User;
import com.example.jamkkaebi.user.repository.UserRepository;
import com.example.jamkkaebi.user.service.UserProfileService;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.time.Duration;
import java.time.LocalDateTime;
import java.util.List;

/**
 * 유물 정화 — 세션 발급과 결과 정산.
 */
@Service
public class PurificationService {

    private final UserProfileService userProfileService;
    private final UserRepository userRepository;
    private final UserRelicRepository userRelicRepository;
    private final PlaySessionRepository sessionRepository;
    private final RelicOwnership ownership;
    private final PlayPlanner planner;
    private final DifficultyCatalog difficulties;
    private final RewardCalculator rewardCalculator;
    private final ClarityPolicy clarityPolicy;
    private final GrowthPolicy growthPolicy;
    private final EraCrystalWallet crystalWallet;
    private final BuildingAwardService buildingAwardService;
    private final SessionTokens sessionTokens;
    private final PurificationProperties properties;
    private final GameClock gameClock;

    public PurificationService(UserProfileService userProfileService,
                               UserRepository userRepository,
                               UserRelicRepository userRelicRepository,
                               PlaySessionRepository sessionRepository,
                               RelicOwnership ownership,
                               PlayPlanner planner,
                               DifficultyCatalog difficulties,
                               RewardCalculator rewardCalculator,
                               ClarityPolicy clarityPolicy,
                               GrowthPolicy growthPolicy,
                               EraCrystalWallet crystalWallet,
                               BuildingAwardService buildingAwardService,
                               SessionTokens sessionTokens,
                               PurificationProperties properties,
                               GameClock gameClock) {
        this.userProfileService = userProfileService;
        this.userRepository = userRepository;
        this.userRelicRepository = userRelicRepository;
        this.sessionRepository = sessionRepository;
        this.ownership = ownership;
        this.planner = planner;
        this.difficulties = difficulties;
        this.rewardCalculator = rewardCalculator;
        this.clarityPolicy = clarityPolicy;
        this.growthPolicy = growthPolicy;
        this.crystalWallet = crystalWallet;
        this.buildingAwardService = buildingAwardService;
        this.sessionTokens = sessionTokens;
        this.properties = properties;
        this.gameClock = gameClock;
    }

    // 난이도 파라미터 목록
    @Transactional(readOnly = true)
    public DifficultyListResponse difficulties() {
        return new DifficultyListResponse(difficulties.ordered().stream()
                .map(difficulty -> DifficultyView.of(difficulty, difficulties.params(difficulty)))
                .toList());
    }

    // 플레이 한 판 시작
    @Transactional
    public PurificationStartResponse start(String friendCode, PurificationStartRequest request) {
        User user = userProfileService.getByFriendCode(friendCode);
        RelicOwnership.Owned owned = ownership.require(user.getId(), request.relicId());
        LocalDateTime now = gameClock.now();
        PlayPlan plan = planner.planFor(owned.userRelic(), now);
        Difficulty difficulty = resolveDifficulty(plan, request.difficulty(),
                owned.userRelic().getAwakeningStage());
        DifficultyParams params = difficulties.params(difficulty);

        sessionRepository.findAllByUserIdAndStatus(user.getId(), SessionStatus.ISSUED)
                .forEach(PlaySession::supersede);

        PlaySession session = sessionRepository.save(PlaySession.builder()
                .sessionToken(sessionTokens.newToken())
                .userId(user.getId())
                .userRelicId(owned.userRelic().getId())
                .phase(plan.phase())
                .difficulty(difficulty)
                .entryRoute(plan.entryRoute())
                .boardSeed(sessionTokens.newBoardSeed())
                .tapBudget(params.tapBudget())
                .scoutLimit(params.scoutLimit())
                .startedAt(now)
                .expiresAt(now.plus(properties.sessionTtl()))
                .build());

        return new PurificationStartResponse(
                session.getSessionToken(),
                session.getExpiresAt(),
                plan.entryRoute(),
                plan.phase(),
                difficulty,
                new BoardView(session.getBoardSeed(), params.boardWidth(), params.boardHeight(),
                        params.threatCount(), params.shapeCount(), params.tapBudget(), params.scoutLimit()),
                params.rewardMultiplier(),
                plan.entryRoute().allowsPerfectBonus());
    }

    // 결과를 보고하고 정산한다.
    @Transactional
    public PurificationResultResponse report(String friendCode, String sessionToken,
                                             PurificationResultRequest request) {
        User user = userProfileService.getByFriendCode(friendCode);
        // 잠그고 읽는다 — 같은 결과가 동시에 두 번 오면 둘 다 ISSUED 를 보고 두 번 정산한다.
        PlaySession session = sessionRepository.findBySessionTokenForUpdate(sessionToken)
                .filter(found -> found.getUserId().equals(user.getId()))
                .orElseThrow(() -> new BusinessException(ErrorCode.PURIFICATION_SESSION_NOT_FOUND));

        validateCombination(request);

        int bonusJeongseong = request.bonusTiles().jeongseong();
        int bonusCrystal = request.bonusTiles().eraCrystal();

        if (session.isSettled()) {
            if (!session.matches(request.result(), request.failReason(), request.tapsUsed(),
                    request.scoutsUsed(), request.damage(), bonusJeongseong, bonusCrystal)) {
                throw new BusinessException(ErrorCode.CONFLICT);
            }
            return render(session, user);
        }

        LocalDateTime now = gameClock.now();
        if (!session.isReportable(now)) {
            throw new BusinessException(ErrorCode.PURIFICATION_SESSION_NOT_FOUND);
        }
        validateLimits(session, request, bonusJeongseong, bonusCrystal, now);

        return settle(session, user, request, bonusJeongseong, bonusCrystal, now);
    }

    private Difficulty resolveDifficulty(PlayPlan plan, Difficulty requested, AwakeningStage stage) {
        if (!plan.difficultySelectable()) {
            if (requested != null) {
                throw invalidDifficulty("최초 정화는 난이도를 고를 수 없습니다.");
            }
            return plan.fixedDifficulty();
        }
        if (requested == null) {
            throw invalidDifficulty("필수 값입니다.");
        }
        difficulties.requireUnlocked(requested, stage);
        return requested;
    }

    private static BusinessException invalidDifficulty(String reason) {
        return new BusinessException(ErrorCode.INVALID_INPUT, List.of(new FieldError("difficulty", reason)));
    }

    private static void validateCombination(PurificationResultRequest request) {
        boolean failure = request.result() == PlayResult.FAILURE;
        if (failure && request.failReason() == null) {
            throw new BusinessException(ErrorCode.INVALID_INPUT,
                    List.of(new FieldError("failReason", "필수 값입니다.")));
        }
        if (!failure && request.failReason() != null) {
            throw new BusinessException(ErrorCode.INVALID_INPUT,
                    List.of(new FieldError("failReason", "성공에는 실패 사유를 보낼 수 없습니다.")));
        }
    }

    private void validateLimits(PlaySession session, PurificationResultRequest request,
                                int bonusJeongseong, int bonusCrystal, LocalDateTime now) {
        int bonusLimit = difficulties.params(session.getDifficulty()).bonusTileLimit();
        boolean overLimit = request.tapsUsed() > session.getTapBudget()
                || request.scoutsUsed() > session.getScoutLimit()
                || bonusJeongseong + bonusCrystal > bonusLimit;
        boolean tooFast = Duration.between(session.getStartedAt(), now)
                .compareTo(properties.minPlayDuration()) < 0;
        if (overLimit || tooFast) {
            throw new BusinessException(ErrorCode.PURIFICATION_RESULT_REJECTED);
        }
    }

    private PurificationResultResponse settle(PlaySession session, User user, PurificationResultRequest request,
                                              int bonusJeongseong, int bonusCrystal, LocalDateTime now) {
        UserRelic userRelic = userRelicRepository.findById(session.getUserRelicId())
                .orElseThrow(() -> new BusinessException(ErrorCode.RELIC_NOT_OWNED));
        RelicMaster master = ownership.require(user.getId(), userRelic.getRelicId()).master();

        boolean success = request.result() == PlayResult.SUCCESS;
        boolean perfect = success && request.damage() == 0 && session.getEntryRoute().allowsPerfectBonus();
        RewardCalculator.Reward reward = rewardCalculator.calculate(
                difficulties.params(session.getDifficulty()), session.getEntryRoute(),
                success, perfect, bonusJeongseong, bonusCrystal);

        boolean completedNow = false;
        if (success) {
            if (session.getEntryRoute() == EntryRoute.FIRST) {
                userRelic.clearFirstPhase(now);
                completedNow = userRelic.isCompleted() && session.getPhase().isLast();
            } else {
                userRelic.clearCarePhase(session.getPhase(), now);
                userRelic.chargeGrowth(reward.gauge());
            }
        }
        if (session.getEntryRoute() == EntryRoute.CARE) {
            userRelic.recordPerfect(perfect);
        }
        if (reward.jeongseong() > 0) {
            userRepository.addJeongseong(user.getId(), reward.jeongseong());
        }
        if (reward.crystal() > 0) {
            crystalWallet.add(user.getId(), master.getEra(), reward.crystal());
        }

        session.settle(PlaySession.Settlement.builder()
                .result(request.result())
                .failReason(request.failReason())
                .tapsUsed(request.tapsUsed())
                .scoutsUsed(request.scoutsUsed())
                .damage(request.damage())
                .bonusJeongseongTiles(bonusJeongseong)
                .bonusCrystalTiles(bonusCrystal)
                .perfect(perfect)
                .jeongseongEarned(reward.jeongseong())
                .crystalEarned(reward.crystal())
                .gaugeEarned(reward.gauge())
                .build(), now);

        // 시대의 마지막 유물을 끝냈다면 건물 자동 지급
        BuildingView awarded = completedNow
                ? buildingAwardService.awardIfEraCompleted(user.getId(), master.getEra(), now)
                .map(building -> BuildingView.of(building.master(), building.owned()))
                .orElse(null)
                : null;

        return render(session, userRelic, master, awarded);
    }

    private PurificationResultResponse render(PlaySession session, User user) {
        UserRelic userRelic = userRelicRepository.findById(session.getUserRelicId())
                .orElseThrow(() -> new BusinessException(ErrorCode.RELIC_NOT_OWNED));
        RelicMaster master = ownership.require(user.getId(), userRelic.getRelicId()).master();
        return render(session, userRelic, master, null);
    }

    private PurificationResultResponse render(PlaySession session, UserRelic userRelic,
                                              RelicMaster master, BuildingView buildingAwarded) {
        LocalDateTime now = gameClock.now();
        boolean success = session.getResult() == PlayResult.SUCCESS;
        boolean first = session.getEntryRoute() == EntryRoute.FIRST;
        Clarity clarity = clarityPolicy.clarityOf(userRelic, now);

        PurificationResultResponse.EraCrystalReward crystalReward = session.getCrystalEarned() > 0
                ? new PurificationResultResponse.EraCrystalReward(master.getEra(), session.getCrystalEarned())
                : null;

        return new PurificationResultResponse(
                session.getResult(),
                Boolean.TRUE.equals(session.getPerfect()),
                first ? null : userRelic.getPerfectStreak(),
                new PurificationResultResponse.Rewards(
                        session.getJeongseongEarned(), crystalReward, session.getGaugeEarned()),
                RelicCard.of(master, userRelic),
                userRelic.getStatus(),
                // 관리 플레이는 공개 단계가 오르지 않는다.
                success && first ? userRelic.revealLevel() : null,
                nextStep(session, userRelic, success, clarity),
                userRelic.getRestorePhase(),
                awakeningView(userRelic),
                clarity,
                buildingAwarded,
                null);
    }

    private PurificationResultResponse.NextStep nextStep(PlaySession session, UserRelic userRelic,
                                                         boolean success, Clarity clarity) {
        if (!success) {
            // 실패로 잃는 것이 없으므로 다음 행동은 항상 "다시 하기"다.
            return PurificationResultResponse.NextStep.of(NextAction.RETRY);
        }
        if (session.getEntryRoute() == EntryRoute.FIRST) {
            if (userRelic.isCompleted()) {
                return PurificationResultResponse.NextStep.of(NextAction.DONE);
            }
            Phase next = userRelic.getCurrentPhase();
            return new PurificationResultResponse.NextStep(NextAction.NEXT_PHASE, next, Phase.from(next));
        }
        if (session.getPhase().isLast()) {
            // 정령 수호를 마쳤으면 선명으로 돌아간다.
            return PurificationResultResponse.NextStep.of(NextAction.DONE);
        }
        List<Phase> remaining = clarityPolicy.remainingPhases(clarity, userRelic.getRestorePhase());
        return new PurificationResultResponse.NextStep(
                NextAction.CONTINUE_RESTORE, remaining.isEmpty() ? null : remaining.getFirst(), remaining);
    }

    private PurificationResultResponse.AwakeningView awakeningView(UserRelic userRelic) {
        if (!userRelic.isCompleted()) {
            return null;
        }
        GaugeView gauge = growthPolicy.hasNextStage(userRelic)
                ? new GaugeView(userRelic.getGrowthGauge(), growthPolicy.requiredFor(userRelic))
                : null;
        return new PurificationResultResponse.AwakeningView(
                userRelic.getAwakeningStage(), gauge, growthPolicy.growthReady(userRelic));
    }
}
