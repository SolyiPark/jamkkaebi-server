package com.example.jamkkaebi.workbench.service;

import com.example.jamkkaebi.artifact.domain.ArtifactMaster;
import com.example.jamkkaebi.artifact.domain.AwakeningStage;
import com.example.jamkkaebi.artifact.domain.BuildingMaster;
import com.example.jamkkaebi.artifact.domain.Clarity;
import com.example.jamkkaebi.artifact.domain.Difficulty;
import com.example.jamkkaebi.artifact.domain.UserArtifact;
import com.example.jamkkaebi.artifact.domain.UserBuilding;
import com.example.jamkkaebi.artifact.dto.response.ArtifactCard;
import com.example.jamkkaebi.artifact.dto.response.BuildingView;
import com.example.jamkkaebi.artifact.dto.response.GaugeView;
import com.example.jamkkaebi.artifact.repository.UserArtifactRepository;
import com.example.jamkkaebi.artifact.repository.UserBuildingRepository;
import com.example.jamkkaebi.artifact.service.ArtifactCatalog;
import com.example.jamkkaebi.artifact.service.ArtifactOwnership;
import com.example.jamkkaebi.artifact.service.ClarityPolicy;
import com.example.jamkkaebi.artifact.service.GrowthPolicy;
import com.example.jamkkaebi.box.config.BoxProperties;
import com.example.jamkkaebi.global.exception.BusinessException;
import com.example.jamkkaebi.global.exception.ErrorCode;
import com.example.jamkkaebi.global.time.GameClock;
import com.example.jamkkaebi.purification.dto.response.DifficultyOption;
import com.example.jamkkaebi.purification.service.DifficultyCatalog;
import com.example.jamkkaebi.purification.service.PlayPlan;
import com.example.jamkkaebi.purification.service.PlayPlanner;
import com.example.jamkkaebi.user.domain.User;
import com.example.jamkkaebi.user.service.UserProfileService;
import com.example.jamkkaebi.workbench.dto.request.AwakenRequest;
import com.example.jamkkaebi.workbench.dto.response.ArtifactDetailResponse;
import com.example.jamkkaebi.workbench.dto.response.AwakenResponse;
import com.example.jamkkaebi.workbench.dto.response.NextPlayView;
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
    private final UserArtifactRepository userArtifactRepository;
    private final UserBuildingRepository userBuildingRepository;
    private final ArtifactCatalog catalog;
    private final ArtifactOwnership ownership;
    private final ClarityPolicy clarityPolicy;
    private final GrowthPolicy growthPolicy;
    private final SpiritNamePolicy spiritNamePolicy;
    private final PlayPlanner planner;
    private final DifficultyCatalog difficulties;
    private final BoxProperties boxProperties;
    private final GameClock gameClock;

    public WorkbenchService(UserProfileService userProfileService,
                            UserArtifactRepository userArtifactRepository,
                            UserBuildingRepository userBuildingRepository,
                            ArtifactCatalog catalog,
                            ArtifactOwnership ownership,
                            ClarityPolicy clarityPolicy,
                            GrowthPolicy growthPolicy,
                            SpiritNamePolicy spiritNamePolicy,
                            PlayPlanner planner,
                            DifficultyCatalog difficulties,
                            BoxProperties boxProperties,
                            GameClock gameClock) {
        this.userProfileService = userProfileService;
        this.userArtifactRepository = userArtifactRepository;
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

        List<UserArtifact> owned = userArtifactRepository.findAllByUserId(user.getId());
        Map<Integer, ArtifactMaster> masters = catalog.artifacts(
                owned.stream().map(UserArtifact::getArtifactId).toList());

        List<SlotView> slots = owned.stream()
                .filter(userArtifact -> !userArtifact.isCompleted())
                .sorted(Comparator.comparing(UserArtifact::getAcquiredAt))
                .map(userArtifact -> SlotView.of(masters.get(userArtifact.getArtifactId()), userArtifact))
                .toList();

        List<SpiritView> spirits = owned.stream()
                .filter(UserArtifact::isCompleted)
                .sorted(careOrder(now))
                .map(userArtifact -> SpiritView.of(
                        masters.get(userArtifact.getArtifactId()),
                        userArtifact,
                        growthPolicy.growthReady(userArtifact),
                        gaugeOf(userArtifact),
                        clarityPolicy.clarityOf(userArtifact, now)))
                .toList();

        return new WorkbenchResponse(
                boxProperties.workbenchCapacity(),
                slots,
                spirits,
                ArtifactMaster.TOTAL - owned.size(),
                buildingsOf(user.getId()));
    }

    // 유물·도깨비 상세.
    @Transactional(readOnly = true)
    public ArtifactDetailResponse detail(String friendCode, Integer artifactId) {
        User user = userProfileService.getByFriendCode(friendCode);
        ArtifactOwnership.Owned owned = ownership.require(user.getId(), artifactId);
        UserArtifact userArtifact = owned.userArtifact();
        LocalDateTime now = gameClock.now();

        boolean completed = userArtifact.isCompleted();
        AwakeningStage stage = userArtifact.getAwakeningStage();

        return new ArtifactDetailResponse(
                ArtifactCard.of(owned.master(), userArtifact),
                userArtifact.getStatus(),
                completed ? null : userArtifact.getCurrentPhase(),
                completed ? stage : null,
                completed ? gaugeOf(userArtifact) : null,
                completed ? growthPolicy.growthReady(userArtifact) : null,
                completed && stage != null ? stage.next() : null,
                clarityPolicy.clarityOf(userArtifact, now),
                userArtifact.getLastPurifiedAt(),
                userArtifact.getRestorePhase(),
                clarityPolicy.resealAt(userArtifact, now),
                userArtifact.isDisplayed(),
                userArtifact.isHidden(),
                nextPlay(userArtifact, now));
    }

    // 도깨비 이름 짓기
    @Transactional
    public SpiritNameResponse renameSpirit(String friendCode, Integer artifactId, String name) {
        User user = userProfileService.getByFriendCode(friendCode);
        ArtifactOwnership.Owned owned = ownership.requireCompleted(user.getId(), artifactId);
        owned.userArtifact().renameSpirit(spiritNamePolicy.validate(name));
        return new SpiritNameResponse(ArtifactCard.of(owned.master(), owned.userArtifact()));
    }

    /**
     * 강화 확정.
     *
     * <p><b>넘친 게이지는 다음 단계로 이월한다</b>
     */
    @Transactional
    public AwakenResponse awaken(String friendCode, Integer artifactId, AwakenRequest request) {
        User user = userProfileService.getByFriendCode(friendCode);
        ArtifactOwnership.Owned owned = ownership.requireCompleted(user.getId(), artifactId);
        UserArtifact userArtifact = owned.userArtifact();
        AwakeningStage current = userArtifact.getAwakeningStage();

        if (request.targetStage() == current) {
            // 응답을 못 받고 다시 누른 경우. 아무것도 바꾸지 않고 지금 상태를 그대로 돌려준다.
            return new AwakenResponse(current, gaugeOf(userArtifact), 0, null, null);
        }
        if (request.targetStage() != current.next()) {
            throw new BusinessException(ErrorCode.AWAKENING_STAGE_MISMATCH);
        }
        if (!growthPolicy.growthReady(userArtifact)) {
            throw new BusinessException(ErrorCode.GROWTH_GAUGE_NOT_FULL);
        }

        int required = growthPolicy.requiredFor(userArtifact);
        int carriedOver = userArtifact.awaken(required);
        AwakeningStage after = userArtifact.getAwakeningStage();

        List<Difficulty> unlocked = difficulties.newlyUnlocked(current, after);
        return new AwakenResponse(
                after,
                gaugeOf(userArtifact),
                after.isMax() ? null : carriedOver,
                unlocked.isEmpty() ? null : unlocked,
                null);
    }

    //돌봐야 할 순서 
    private Comparator<UserArtifact> careOrder(LocalDateTime now) {
        return Comparator
                .comparingInt((UserArtifact userArtifact) -> careRank(userArtifact, now))
                .thenComparing(UserArtifact::getArtifactId);
    }

    private int careRank(UserArtifact userArtifact, LocalDateTime now) {
        Clarity clarity = clarityPolicy.clarityOf(userArtifact, now);
        if (clarity == Clarity.RESEALED) {
            return 0;
        }
        if (clarity == Clarity.FADED) {
            return 1;
        }
        return growthPolicy.growthReady(userArtifact) ? 2 : 3;
    }

    private GaugeView gaugeOf(UserArtifact userArtifact) {
        if (!growthPolicy.hasNextStage(userArtifact)) {
            return null;
        }
        return new GaugeView(userArtifact.getGrowthGauge(), growthPolicy.requiredFor(userArtifact));
    }

    private NextPlayView nextPlay(UserArtifact userArtifact, LocalDateTime now) {
        PlayPlan plan = planner.planFor(userArtifact, now);
        List<DifficultyOption> options = new ArrayList<>();
        if (plan.difficultySelectable()) {
            for (Difficulty difficulty : difficulties.ordered()) {
                options.add(new DifficultyOption(difficulty,
                        difficulties.unlocked(difficulty, userArtifact.getAwakeningStage())));
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
