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
import com.example.jamkkaebi.relic.domain.Clarity;
import com.example.jamkkaebi.relic.domain.Difficulty;
import com.example.jamkkaebi.relic.domain.EntryRoute;
import com.example.jamkkaebi.relic.domain.Phase;
import com.example.jamkkaebi.relic.domain.RelicMaster;
import com.example.jamkkaebi.relic.domain.RelicStatus;
import com.example.jamkkaebi.relic.domain.RevealLevel;
import com.example.jamkkaebi.relic.domain.UserRelic;
import com.example.jamkkaebi.relic.dto.response.BuildingView;
import com.example.jamkkaebi.relic.dto.response.RelicCard;
import com.example.jamkkaebi.relic.repository.UserRelicRepository;
import com.example.jamkkaebi.relic.service.BuildingAwardService;
import com.example.jamkkaebi.relic.service.ClarityPolicy;
import com.example.jamkkaebi.relic.service.RelicOwnership;
import com.example.jamkkaebi.user.domain.User;
import com.example.jamkkaebi.user.repository.UserRepository;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.time.Duration;
import java.time.LocalDateTime;
import java.util.List;

/**
 * 유물 정화
 */
@Service
public class PurificationService {

    private final UserRepository userRepository;
    private final UserRelicRepository userRelicRepository;
    private final PlaySessionRepository sessionRepository;
    private final RelicOwnership ownership;
    private final PlayPlanner planner;
    private final DifficultyCatalog difficulties;
    private final RewardCalculator rewardCalculator;
    private final ClarityPolicy clarityPolicy;
    private final RestorePolicy restorePolicy;
    private final BuildingAwardService buildingAwardService;
    private final SessionTokens sessionTokens;
    private final PurificationProperties properties;
    private final GameClock gameClock;

    public PurificationService(UserRepository userRepository,
                               UserRelicRepository userRelicRepository,
                               PlaySessionRepository sessionRepository,
                               RelicOwnership ownership,
                               PlayPlanner planner,
                               DifficultyCatalog difficulties,
                               RewardCalculator rewardCalculator,
                               ClarityPolicy clarityPolicy,
                               RestorePolicy restorePolicy,
                               BuildingAwardService buildingAwardService,
                               SessionTokens sessionTokens,
                               PurificationProperties properties,
                               GameClock gameClock) {
        this.userRepository = userRepository;
        this.userRelicRepository = userRelicRepository;
        this.sessionRepository = sessionRepository;
        this.ownership = ownership;
        this.planner = planner;
        this.difficulties = difficulties;
        this.rewardCalculator = rewardCalculator;
        this.clarityPolicy = clarityPolicy;
        this.restorePolicy = restorePolicy;
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
        // 사용자 행을 이 트랜잭션의 첫 읽기로 잠근다.
        User user = lockUser(friendCode);
        RelicOwnership.Owned owned = ownership.require(user.getId(), request.relicId());
        LocalDateTime now = gameClock.now();
        PlayPlan plan = planner.planFor(owned.userRelic(), now);
        Difficulty difficulty = resolveDifficulty(plan, request.difficulty());
        DifficultyParams params = difficulties.params(difficulty);

        sessionRepository.findAllByUserIdAndStatus(user.getId(), SessionStatus.ISSUED)
                .forEach(PlaySession::supersede);

        PlaySession session = sessionRepository.save(PlaySession.builder()
                .sessionToken(sessionTokens.newToken())
                .userId(user.getId())
                .userRelicId(owned.userRelic().getId())
                .phase(plan.phase())
                .planClarity(plan.clarity())
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
                owned.master().getSealMedium(),
                boardOf(session.getBoardSeed(), params),
                params.rewardMultiplier(),
                plan.entryRoute().allowsPerfectBonus());
    }

    // 같은 시드·같은 난이도면 같은 보드
    private BoardView boardOf(long seed, DifficultyParams params) {
        return new BoardView(seed, params.boardWidth(), params.boardHeight(),
                params.threatCount(), params.reinforcedOnShape(), params.reinforcedOnField(),
                params.helperCount(), params.bonusJeongseongCount(),
                params.shapeCount(), params.shapeCellTotal(),
                params.tapBudget(), params.scoutLimit());
    }

    // 결과를 보고하고 정산
    @Transactional
    public PurificationResultResponse report(String friendCode, String sessionToken,
                                             PurificationResultRequest request) {
        // 시작과 같은 순서(사용자 → 세션)로 잠근다.
        User user = lockUser(friendCode);

        PlaySession session = sessionRepository.findBySessionTokenForUpdate(sessionToken)
                .filter(found -> found.getUserId().equals(user.getId()))
                .orElseThrow(() -> new BusinessException(ErrorCode.PURIFICATION_SESSION_NOT_FOUND));

        validateCombination(request);

        int bonusJeongseong = request.bonusTiles().jeongseong();

        if (session.isSettled()) {
            if (!session.matches(request.result(), request.failReason(), request.tapsUsed(),
                    request.scoutsUsed(), request.damage(), bonusJeongseong)) {
                throw new BusinessException(ErrorCode.CONFLICT);
            }
            return render(session, user);
        }

        LocalDateTime now = gameClock.now();
        if (!session.isReportable(now)) {
            throw new BusinessException(ErrorCode.PURIFICATION_SESSION_NOT_FOUND);
        }
        validateLimits(session, request, bonusJeongseong, now);

        return settle(session, user, request, bonusJeongseong, now);
    }

    // 사용자 행을 잠그고 읽는다.
    private User lockUser(String friendCode) {
        return userRepository.findByFriendCodeForUpdate(friendCode)
                .orElseThrow(() -> new BusinessException(ErrorCode.USER_NOT_FOUND));
    }

    // 계획이 허용한 난이도만 받는다. 하나뿐이면(최초 정화 등) 생략해도 그 난이도로 정한다.
    private static Difficulty resolveDifficulty(PlayPlan plan, Difficulty requested) {
        if (requested == null) {
            Difficulty fixed = plan.fixedDifficulty();
            if (fixed == null) {
                throw invalidDifficulty("필수 값입니다.");
            }
            return fixed;
        }
        if (!plan.allows(requested)) {
            throw new BusinessException(ErrorCode.DIFFICULTY_NOT_ALLOWED);
        }
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
                                int bonusJeongseong, LocalDateTime now) {
        DifficultyParams params = difficulties.params(session.getDifficulty());
        boolean overLimit = request.tapsUsed() > session.getTapBudget()
                || request.scoutsUsed() > session.getScoutLimit()
                || bonusJeongseong > params.bonusJeongseongCount();
        boolean tooFast = Duration.between(session.getStartedAt(), now)
                .compareTo(properties.minPlayDuration()) < 0;
        if (overLimit || tooFast) {
            throw new BusinessException(ErrorCode.PURIFICATION_RESULT_REJECTED);
        }
    }

    private PurificationResultResponse settle(PlaySession session, User user, PurificationResultRequest request,
                                              int bonusJeongseong, LocalDateTime now) {
        UserRelic userRelic = userRelicRepository.findById(session.getUserRelicId())
                .orElseThrow(() -> new BusinessException(ErrorCode.RELIC_NOT_OWNED));
        RelicMaster master = ownership.require(user.getId(), userRelic.getRelicId()).master();

        // 계획 선명도가 없는 옛 세션은 유물을 바꾸기 전의 선명도로 출발점을 정해 둔다(정산 뒤에 다시 계산하면 두 번 오른다).
        session.fillPlanClarity(clarityPolicy.clarityOf(userRelic, now));

        boolean success = request.result() == PlayResult.SUCCESS;
        boolean perfect = success && request.damage() == 0 && session.getEntryRoute().allowsPerfectBonus();
        RewardCalculator.Reward reward = rewardCalculator.calculate(
                difficulties.params(session.getDifficulty()), success, perfect, bonusJeongseong);

        boolean completedNow = false;
        if (success) {
            if (session.getEntryRoute() == EntryRoute.FIRST) {
                userRelic.clearFirstPlay(now);
                completedNow = userRelic.isCompleted() && session.getPhase().isLast();
            } else {
                settleCare(session, userRelic, now);
            }
        }
        if (session.getEntryRoute() == EntryRoute.CARE) {
            userRelic.recordPerfect(perfect);
        }
        if (reward.jeongseong() > 0) {
            userRepository.addJeongseong(user.getId(), reward.jeongseong());
        }

        session.settle(PlaySession.Settlement.builder()
                .result(request.result())
                .failReason(request.failReason())
                .tapsUsed(request.tapsUsed())
                .scoutsUsed(request.scoutsUsed())
                .damage(request.damage())
                .bonusJeongseongTiles(bonusJeongseong)
                .perfect(perfect)
                .jeongseongEarned(reward.jeongseong())
                .build(), now);

        // 시대의 마지막 유물을 끝냈다면 건물 지급
        BuildingView awarded = completedNow
                ? buildingAwardService.awardIfEraCompleted(user.getId(), master.getEra(), now)
                .map(building -> BuildingView.of(building.master(), building.owned()))
                .orElse(null)
                : null;

        return render(session, userRelic, master, awarded);
    }

    /**
     * 관리 플레이 성공. 선명도를 {@link #reachedClarity} 까지 올리고 타이머를 새로 시작한다.
     */
    private void settleCare(PlaySession session, UserRelic userRelic, LocalDateTime now) {
        Clarity reached = reachedClarity(session, userRelic, now);
        userRelic.clearCarePlay(clarityPolicy.anchorFor(reached, now), now);
    }

    /**
     * 관리 플레이 한 판 성공으로 오르는 선명도. 보통은 한 칸(재봉인 → 흐려짐 → 선명), 어려움·쉬움은 바로 선명이다.
     *
     * <p>출발점은 <b>세션 시작 시점 계획의 선명도</b>다. 플레이 도중 7·14일 경계를 넘어 떨어졌어도 시작할 때의
     * 선명도에서 한 칸 올린다(관대한 쪽). 세션 값만으로 정해지므로 같은 결과를 다시 보고해도 같은 값이 나온다.
     */
    private Clarity reachedClarity(PlaySession session, UserRelic userRelic, LocalDateTime now) {
        if (restorePolicy.effectOf(session.getDifficulty()) != RestoreEffect.ONE_STEP) {
            return Clarity.CLEAR;
        }
        Clarity planned = session.getPlanClarity();
        if (planned == null) {
            // 이 수정 전에 계획 선명도 없이 정산된 세션을 같은 내용으로 다시 보고한 경우뿐이다.
            // 유물은 이미 올라 있으므로 다시 올리지 않고 지금 선명도를 쓴다.
            return clarityPolicy.clarityOf(userRelic, now);
        }
        return planned.stepUp();
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

        return new PurificationResultResponse(
                session.getResult(),
                Boolean.TRUE.equals(session.getPerfect()),
                first ? null : userRelic.getPerfectStreak(),
                new PurificationResultResponse.Rewards(session.getJeongseongEarned()),
                RelicCard.of(master, userRelic),
                userRelic.getStatus(),
                // 관리 플레이(!first)는 공개 단계가 오르지 않는다.
                success && first ? revealedBy(session.getPhase()) : null,
                nextStep(session, userRelic, success, now),
                userRelic.isCompleted() ? userRelic.getCareCount() : null,
                clarity,
                userRelic.isCompleted() ? userRelic.getLastPurifiedAt() : null,
                clarityPolicy.nextChangeAt(userRelic, now),
                buildingAwarded,
                null);
    }

    /**
     * 최초 정화 한 판의 성공으로 <b>새로</b> 열리는 공개 단계. 봉인 해제는 {@code FORM}, 기억 회복은 {@code NAMED}이고,
     * 오염 관리는 이미 이름까지 공개된 뒤라 없다.
     *
     * <p>유물의 지금 공개 단계가 아니라 세션의 페이즈로 정한다. 다음 페이즈를 진행한 뒤 이전 판을 다시 보고해도
     * 처음 정산과 같은 값이 나온다.
     */
    private static RevealLevel revealedBy(Phase cleared) {
        return cleared.isLast() ? null : RevealLevel.of(RelicStatus.IN_PROGRESS, cleared.next());
    }

    private PurificationResultResponse.NextStep nextStep(PlaySession session, UserRelic userRelic,
                                                         boolean success, LocalDateTime now) {
        if (!success) {
            //실패 하면 재시도(잃는 것 없음)
            return PurificationResultResponse.NextStep.of(NextAction.RETRY);
        }
        if (session.getEntryRoute() == EntryRoute.FIRST) {
            if (userRelic.isCompleted()) {
                return PurificationResultResponse.NextStep.of(NextAction.DONE);
            }
            Phase next = userRelic.getCurrentPhase();
            return new PurificationResultResponse.NextStep(NextAction.NEXT_PHASE, next, Phase.from(next));
        }
        // 선명이 되면 복구가 끝난다. 아니면(재봉인 → 흐려짐) 오른 선명도의 페이즈를 이어서 한다.
        Clarity reached = reachedClarity(session, userRelic, now);
        if (reached == Clarity.CLEAR) {
            return PurificationResultResponse.NextStep.of(NextAction.DONE);
        }
        return new PurificationResultResponse.NextStep(
                NextAction.CONTINUE_RESTORE, reached.restoreFrom(), clarityPolicy.remainingPhases(reached));
    }
}
