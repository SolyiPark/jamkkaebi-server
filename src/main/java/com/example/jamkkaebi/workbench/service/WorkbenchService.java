package com.example.jamkkaebi.workbench.service;

import com.example.jamkkaebi.box.config.BoxProperties;
import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import com.example.jamkkaebi.global.time.GameClock;
import com.example.jamkkaebi.purification.dto.response.DifficultyOption;
import com.example.jamkkaebi.purification.service.DifficultyCatalog;
import com.example.jamkkaebi.purification.service.PlayPlan;
import com.example.jamkkaebi.purification.service.PlayPlanner;
import com.example.jamkkaebi.relic.domain.AwakeningStage;
import com.example.jamkkaebi.relic.domain.BuildingMaster;
import com.example.jamkkaebi.relic.domain.Clarity;
import com.example.jamkkaebi.relic.domain.Difficulty;
import com.example.jamkkaebi.relic.domain.RelicMaster;
import com.example.jamkkaebi.relic.domain.UserBuilding;
import com.example.jamkkaebi.relic.domain.UserRelic;
import com.example.jamkkaebi.relic.dto.response.BuildingView;
import com.example.jamkkaebi.relic.dto.response.GaugeView;
import com.example.jamkkaebi.relic.dto.response.RelicCard;
import com.example.jamkkaebi.relic.repository.UserBuildingRepository;
import com.example.jamkkaebi.relic.repository.UserRelicRepository;
import com.example.jamkkaebi.relic.service.ClarityPolicy;
import com.example.jamkkaebi.relic.service.GrowthPolicy;
import com.example.jamkkaebi.relic.service.RelicCatalog;
import com.example.jamkkaebi.relic.service.RelicOwnership;
import com.example.jamkkaebi.user.domain.User;
import com.example.jamkkaebi.user.service.UserProfileService;
import com.example.jamkkaebi.workbench.dto.request.AwakenRequest;
import com.example.jamkkaebi.workbench.dto.response.AwakenResponse;
import com.example.jamkkaebi.workbench.dto.response.NextPlayView;
import com.example.jamkkaebi.workbench.dto.response.RelicDetailResponse;
import com.example.jamkkaebi.workbench.dto.response.SlotView;
import com.example.jamkkaebi.workbench.dto.response.SpiritNameResponse;
import com.example.jamkkaebi.workbench.dto.response.SpiritView;
import com.example.jamkkaebi.workbench.dto.response.WorkbenchResponse;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.time.LocalDateTime;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Map;

/**
 * 작업대 — 미완료 유물 진행과 도깨비 성장 확정.
 *
 * <p><b>강화는 여기서만 확정된다.</b>
 * 게이지가 가득 차도 자동으로 승급하지 않는 이유는, 승급 자체가 사용자가 보는 연출이자 보상이기 때문이다.
 */
@Service
public class WorkbenchService {

    private final UserProfileService userProfileService;
    private final UserRelicRepository userRelicRepository;
    private final UserBuildingRepository userBuildingRepository;
    private final RelicCatalog catalog;
    private final RelicOwnership ownership;
    private final ClarityPolicy clarityPolicy;
    private final GrowthPolicy growthPolicy;
    private final SpiritNamePolicy spiritNamePolicy;
    private final PlayPlanner planner;
    private final DifficultyCatalog difficulties;
    private final BoxProperties boxProperties;
    private final GameClock gameClock;

    public WorkbenchService(UserProfileService userProfileService,
                            UserRelicRepository userRelicRepository,
                            UserBuildingRepository userBuildingRepository,
                            RelicCatalog catalog,
                            RelicOwnership ownership,
                            ClarityPolicy clarityPolicy,
                            GrowthPolicy growthPolicy,
                            SpiritNamePolicy spiritNamePolicy,
                            PlayPlanner planner,
                            DifficultyCatalog difficulties,
                            BoxProperties boxProperties,
                            GameClock gameClock) {
        this.userProfileService = userProfileService;
        this.userRelicRepository = userRelicRepository;
        this.userBuildingRepository = userBuildingRepository;
        this.catalog = catalog;
        this.ownership = ownership;
        this.clarityPolicy = clarityPolicy;
        this.growthPolicy = growthPolicy;
        this.spiritNamePolicy = spiritNamePolicy;
        this.planner = planner;
        this.difficulties = difficulties;
        this.boxProperties = boxProperties;
        this.gameClock = gameClock;
    }

    // 작업대 첫 화면 — 미확인 슬롯, 도깨비, 건물
    @Transactional(readOnly = true)
    public WorkbenchResponse workbench(String friendCode) {
        User user = userProfileService.getByFriendCode(friendCode);
        LocalDateTime now = gameClock.now();

        List<UserRelic> owned = userRelicRepository.findAllByUserId(user.getId());
        Map<Integer, RelicMaster> masters = catalog.relics(
                owned.stream().map(UserRelic::getRelicId).toList());

        List<SlotView> slots = owned.stream()
                .filter(userRelic -> !userRelic.isCompleted())
                .sorted(Comparator.comparing(UserRelic::getAcquiredAt))
                .map(userRelic -> SlotView.of(masters.get(userRelic.getRelicId()), userRelic))
                .toList();

        List<SpiritView> spirits = owned.stream()
                .filter(UserRelic::isCompleted)
                .sorted(careOrder(now))
                .map(userRelic -> SpiritView.of(
                        masters.get(userRelic.getRelicId()),
                        userRelic,
                        growthPolicy.growthReady(userRelic),
                        gaugeOf(userRelic),
                        clarityPolicy.clarityOf(userRelic, now)))
                .toList();

        return new WorkbenchResponse(
                boxProperties.workbenchCapacity(),
                slots,
                spirits,
                RelicMaster.TOTAL - owned.size(),
                buildingsOf(user.getId()));
    }

    // 유물·도깨비 상세.
    @Transactional(readOnly = true)
    public RelicDetailResponse detail(String friendCode, Integer relicId) {
        User user = userProfileService.getByFriendCode(friendCode);
        RelicOwnership.Owned owned = ownership.require(user.getId(), relicId);
        UserRelic userRelic = owned.userRelic();
        LocalDateTime now = gameClock.now();

        boolean completed = userRelic.isCompleted();
        AwakeningStage stage = userRelic.getAwakeningStage();

        return new RelicDetailResponse(
                RelicCard.of(owned.master(), userRelic),
                userRelic.getStatus(),
                completed ? null : userRelic.getCurrentPhase(),
                completed ? stage : null,
                completed ? gaugeOf(userRelic) : null,
                completed ? growthPolicy.growthReady(userRelic) : null,
                completed && stage != null ? stage.next() : null,
                clarityPolicy.clarityOf(userRelic, now),
                userRelic.getLastPurifiedAt(),
                userRelic.getRestorePhase(),
                clarityPolicy.resealAt(userRelic, now),
                userRelic.isDisplayed(),
                userRelic.isHidden(),
                nextPlay(userRelic, now));
    }

    // 도깨비 이름 짓기
    @Transactional
    public SpiritNameResponse renameSpirit(String friendCode, Integer relicId, String name) {
        User user = userProfileService.getByFriendCode(friendCode);
        RelicOwnership.Owned owned = ownership.requireCompleted(user.getId(), relicId);
        owned.userRelic().renameSpirit(spiritNamePolicy.validate(name));
        return new SpiritNameResponse(RelicCard.of(owned.master(), owned.userRelic()));
    }

    /**
     * 강화 확정.
     *
     * <p><b>넘친 게이지는 다음 단계로 이월한다</b>
     */
    @Transactional
    public AwakenResponse awaken(String friendCode, Integer relicId, AwakenRequest request) {
        User user = userProfileService.getByFriendCode(friendCode);
        RelicOwnership.Owned owned = ownership.requireCompleted(user.getId(), relicId);
        UserRelic userRelic = owned.userRelic();
        AwakeningStage current = userRelic.getAwakeningStage();

        if (request.targetStage() == current) {
            // 응답을 못 받고 다시 누른 경우. 아무것도 바꾸지 않고 지금 상태를 그대로 돌려준다.
            return new AwakenResponse(current, gaugeOf(userRelic), 0, null, null);
        }
        if (request.targetStage() != current.next()) {
            throw new BusinessException(ErrorCode.AWAKENING_STAGE_MISMATCH);
        }
        if (!growthPolicy.growthReady(userRelic)) {
            throw new BusinessException(ErrorCode.GROWTH_GAUGE_NOT_FULL);
        }

        int required = growthPolicy.requiredFor(userRelic);
        int carriedOver = userRelic.awaken(required);
        AwakeningStage after = userRelic.getAwakeningStage();

        List<Difficulty> unlocked = difficulties.newlyUnlocked(current, after);
        return new AwakenResponse(
                after,
                gaugeOf(userRelic),
                after.isMax() ? null : carriedOver,
                unlocked.isEmpty() ? null : unlocked,
                null);
    }

    //돌봐야 할 순서 
    private Comparator<UserRelic> careOrder(LocalDateTime now) {
        return Comparator
                .comparingInt((UserRelic userRelic) -> careRank(userRelic, now))
                .thenComparing(UserRelic::getRelicId);
    }

    private int careRank(UserRelic userRelic, LocalDateTime now) {
        Clarity clarity = clarityPolicy.clarityOf(userRelic, now);
        if (clarity == Clarity.RESEALED) {
            return 0;
        }
        if (clarity == Clarity.FADED) {
            return 1;
        }
        return growthPolicy.growthReady(userRelic) ? 2 : 3;
    }

    private GaugeView gaugeOf(UserRelic userRelic) {
        if (!growthPolicy.hasNextStage(userRelic)) {
            return null;
        }
        return new GaugeView(userRelic.getGrowthGauge(), growthPolicy.requiredFor(userRelic));
    }

    private NextPlayView nextPlay(UserRelic userRelic, LocalDateTime now) {
        PlayPlan plan = planner.planFor(userRelic, now);
        List<DifficultyOption> options = new ArrayList<>();
        if (plan.difficultySelectable()) {
            for (Difficulty difficulty : difficulties.ordered()) {
                options.add(new DifficultyOption(difficulty,
                        difficulties.unlocked(difficulty, userRelic.getAwakeningStage())));
            }
        } else {
            options.add(new DifficultyOption(plan.fixedDifficulty(), true));
        }
        return new NextPlayView(plan.entryRoute(), plan.phase(), plan.remainingPhases(),
                plan.difficultySelectable(), options);
    }

    private List<BuildingView> buildingsOf(Long userId) {
        List<UserBuilding> owned = userBuildingRepository.findAllByUserId(userId);
        Map<Integer, BuildingMaster> masters = catalog.buildings(
                owned.stream().map(UserBuilding::getBuildingId).toList());
        return owned.stream()
                .map(building -> BuildingView.of(masters.get(building.getBuildingId()), building))
                .sorted(Comparator.comparing(BuildingView::buildingId))
                .toList();
    }
}
